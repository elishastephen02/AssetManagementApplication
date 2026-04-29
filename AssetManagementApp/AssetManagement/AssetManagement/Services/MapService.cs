using Microsoft.AspNetCore.Http.Features;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using NuGet.Packaging.Signing;
using SQLitePCL;

namespace AssetManagement.Services
{
    public class MapService
    {
        private readonly DataService _db;

        public MapService(DataService db)
        {
            _db = db;
        }

        public object GetMapData()
        {
            var geoRows = _db.Query(@"
                SELECT
                    SEGID,
                    RoadName,
                    MATERIAL,
                    InspectedL  AS InspectedLength,
                    InspectedD  AS InspectedDate,
                    STR_SCORE,
                    SER_SCORE,
                    GEOMETRY_DATA.STAsText() AS GeometryWKT
                FROM GEOJSON
                WHERE GEOMETRY_DATA IS NOT NULL
            ").ToList();

            var dbSections = _db.Query(@"
                SELECT
                    s.OBJ_PK           AS Id,
                    s.OBJ_Key          AS SegId,
                    s.OBJ_Size1        AS Size,
                    s.OBJ_Material     AS Material,
                    s.OBJ_Spare4       AS Status,
                    s.OBJ_Spare5       AS Owner,

                    si.INS_PK              AS InspectionId,
                    si.INS_StartDate       AS LastInspection,
                    si.INS_InspectedLength AS InspectedLength,

                    ss.STA_HighestGrade AS HighestGrade,
                    ss.STA_TotalScore   AS TotalScore,
                    ss.STA_PeakScore    AS PeakScore
                FROM SECTION s
                LEFT JOIN SECINSP si ON s.OBJ_PK = si.INS_Section_FK
                LEFT JOIN SECSTAT ss ON si.INS_PK = ss.STA_Inspection_FK
            ").ToList();

            var dbLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in dbSections)
            {
                string key = Convert.ToString(row.SegId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!dbLookup.ContainsKey(key)) dbLookup[key] = new List<dynamic>();
                dbLookup[key].Add(row);
            }

            var inspectionIds = dbSections
                .Select(p => Convert.ToString(p.InspectionId ?? ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct().ToList();

            List<dynamic> observations = new();
            List<dynamic> media = new();

            if (inspectionIds.Any())
            {
                string insIdList = string.Join(",", inspectionIds.Select(id => $"'{id}'"));

                observations = _db.Query($@"
                    SELECT
                        OBS_PK            AS ObsId,
                        OBS_Inspection_FK AS InspectionId,
                        OBS_Distance      AS Distance,
                        OBS_Observation   AS Observation,
                        OBS_GradeS        AS Grade,
                        OBS_ScoreS        AS Score
                    FROM SECOBS
                    WHERE OBS_Inspection_FK IN ({insIdList})
                ").ToList();

                var obsIds = observations
                    .Select(o => Convert.ToString(o.ObsId ?? ""))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct().ToList();

                if (obsIds.Any())
                {
                    string obsIdList = string.Join(",", obsIds.Select(id => $"'{id}'"));
                    media = _db.Query($@"
                        SELECT
                            OMM_Observation_FK AS ObsId,
                            OMM_FileName       AS FileName,
                            OMM_FileType       AS FileType
                        FROM SECOBSMM
                        WHERE OMM_Observation_FK IN ({obsIdList})
                    ").ToList();
                }
            }

            var sections = new List<object>();
            var summary = new List<object>();

            foreach (var geo in geoRows)
            {
                string segId = Convert.ToString(geo.SEGID ?? "");
                if (string.IsNullOrEmpty(segId)) continue;

                sections.Add(new { OBJ_PK = segId, Geometry = (string)geo.GeometryWKT });

                dbLookup.TryGetValue(segId, out var dbRows);

                var firstRow = dbRows?
                    .OrderByDescending(x => x.LastInspection)
                    .FirstOrDefault();

                // Build inspections list
                var inspections = new List<object>();
                if (dbRows != null)
                {
                    int inspNum = 1;
                    foreach (var insp in dbRows.Where(r => r.InspectionId != null))
                    {
                        string insId = Convert.ToString(insp.InspectionId ?? "");

                        var pipeObs = !string.IsNullOrEmpty(insId)
                            ? observations
                                .Where(o => Convert.ToString(o.InspectionId ?? "") == insId)
                                .OrderBy(o => {
                                    decimal d;
                                    return decimal.TryParse(Convert.ToString(o.Distance ?? ""), out d) ? d : decimal.MaxValue;
                                })
                                .ToList()
                            : new List<dynamic>();

                        var obsWithMedia = pipeObs.Select(o =>
                        {
                            string obsId = Convert.ToString(o.ObsId ?? "");
                            var obsMedia = !string.IsNullOrEmpty(obsId)
                                ? media.Where(m => Convert.ToString(m.ObsId ?? "") == obsId).ToList()
                                : new List<dynamic>();

                            return new
                            {
                                o.Distance,
                                o.Observation,
                                o.Grade,
                                o.Score,
                                Media = obsMedia.Select(m => new { m.FileName, m.FileType }).ToList()
                            };
                        }).ToList();

                        inspections.Add(new
                        {
                            InspectionNumber = inspNum++,
                            InspectionDate = insp.LastInspection,   // INS_StartDate
                            InspectedLength = insp.InspectedLength,  // INS_InspectedLength
                            HighestGrade = insp.HighestGrade,     // STA_HighestGrade
                            TotalScore = insp.TotalScore,       // STA_TotalScore
                            PeakScore = insp.PeakScore,        // STA_PeakScore
                            Observations = obsWithMedia
                        });
                    }
                }

                bool hasInspection = firstRow?.InspectionId != null;

                summary.Add(new
                {
                    Id = segId,
                    Name = segId,

                    // Prefer SECTION, fall back to GEOJSON
                    Material = firstRow?.Material != null
                                        ? Convert.ToString(firstRow.Material)
                                        : Convert.ToString(geo.MATERIAL ?? ""),

                    Address = Convert.ToString(geo.RoadName ?? ""),

                    PipeDiameter = firstRow?.Size,                // OBJ_Size1 — only in SECTION

                    LastInspection = firstRow?.LastInspection       // INS_StartDate
                                      ?? geo.InspectedDate,          // GEOJSON.InspectedD

                    InspectedLength = firstRow?.InspectedLength      // INS_InspectedLength
                                      ?? geo.InspectedLength,        // GEOJSON.InspectedL

                    HighestGrade = firstRow?.HighestGrade,        // STA_HighestGrade

                    TotalScore = firstRow?.TotalScore           // STA_TotalScore
                                      ?? geo.STR_SCORE,              // GEOJSON.STR_SCORE

                    PeakScore = firstRow?.PeakScore            // STA_PeakScore
                                      ?? geo.SER_SCORE,              // GEOJSON.SER_SCORE

                    Condition = hasInspection ? "Inspected" : "Uninspected",
                    Inspections = inspections
                });
            }

            return new { sections, summary };
        }

    }
}