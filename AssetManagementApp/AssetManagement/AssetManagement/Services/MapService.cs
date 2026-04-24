using Microsoft.SqlServer.Types;
using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace AssetManagement.Services
{
    public class MapService
    {
        private readonly DataService _db;

        public MapService(DataService db)
        {
            _db = db;
        }

        public object ProcessGeoJson(string geoJson)
        {
            var reader = new GeoJsonReader();
            var featureCollection = reader.Read<FeatureCollection>(geoJson);

            // Load ALL inspections per section (not just one)
            var dbSections = _db.Query(@"
                SELECT 
                    s.OBJ_PK       AS Id,
                    s.OBJ_Key      AS SegId,
                    s.OBJ_Size1    AS Size,
                    s.OBJ_Material AS Material,
                    s.OBJ_Spare4   AS Status,

                    si.INS_PK              AS InspectionId,
                    si.INS_StartDate       AS LastInspection,
                    si.INS_InspectedLength AS InspectedLength,

                    ss.STA_Type           AS StaType,
                    ss.STA_HighestGrade   AS HighestGrade, 
                    ss.STA_TotalScore AS TotalScore,
                    ss.STA_PeakScore  AS PeakScore

                FROM SECTION s
                LEFT JOIN SECINSP si ON s.OBJ_PK = si.INS_Section_FK
                LEFT JOIN SECSTAT ss ON si.INS_PK = ss.STA_Inspection_FK
            ").ToList();

            // Lookup: SegId -> list of inspection rows
            var dbLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in dbSections)
            {
                var key = row.SegId as string;
                if (string.IsNullOrEmpty(key)) continue;
                if (!dbLookup.ContainsKey(key)) dbLookup[key] = new List<dynamic>();
                dbLookup[key].Add(row);
            }

            // Load all observations
            var inspectionIds = dbSections
                .Select(p => p.InspectionId?.ToString())
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
                    .Select(o => o.ObsId?.ToString())
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

            foreach (var feature in featureCollection)
            {
                var segId = feature.Attributes["SEGID"]?.ToString();
                if (string.IsNullOrEmpty(segId)) continue;

                var wktWriter = new WKTWriter();
                var wkt = wktWriter.Write(feature.Geometry);
                sections.Add(new { OBJ_PK = segId, Geometry = wkt });

                dbLookup.TryGetValue(segId, out var dbRows);
                var firstRow = dbRows?
                    .OrderByDescending(x => x.LastInspection)
                    .FirstOrDefault();

                // Build inspections list — one entry per INS_PK
                var inspections = new List<object>();
                if (dbRows != null)
                {
                    int inspNum = 1;
                    foreach (var insp in dbRows.Where(r => r.InspectionId != null))
                    {
                        var insId = insp.InspectionId?.ToString();

                        var pipeObs = !string.IsNullOrEmpty(insId)
                            ? observations.Where(o => o.InspectionId?.ToString() == insId).ToList()
                            : new List<dynamic>();

                        var obsWithMedia = pipeObs.Select(o =>
                        {
                            var obsId = o.ObsId?.ToString();
                            var obsMedia = !string.IsNullOrEmpty(obsId)
                                ? media.Where(m => m.ObsId?.ToString() == obsId).ToList()
                                : new List<dynamic>();

                            return new
                            {
                                o.Distance,
                                o.Observation,
                                o.Grade,
                                o.Score,
                                Media = obsMedia.Select(m => new { m.FileName, m.FileType })
                            };
                        }).ToList();

                        inspections.Add(new
                        {
                            InspectionNumber = inspNum++,
                            InspectionDate = insp.LastInspection,
                            InspectedLength = insp.InspectedLength,
                            TotalScore = insp.TotalScore,
                            PeakScore = insp.PeakScore,
                            Observations = obsWithMedia
                        });
                    }
                }

                bool hasInspection = firstRow?.InspectionId != null;
                string condition = hasInspection ? "Inspected" : "Uninspected";

                summary.Add(new
                {
                    Id = segId,
                    Name = firstRow?.SegId as string ?? segId,

                    Material = firstRow?.Material as string
                    ?? (feature.Attributes.Exists("MATERIAL")
                    ? feature.Attributes["MATERIAL"]?.ToString()
                    : null),

                    Address = firstRow?.Address as string
                    ?? (feature.Attributes.Exists("RoadName")
                    ? feature.Attributes["RoadName"]?.ToString()
                    : null),

                    LastInspection = firstRow?.LastInspection,

                    PipeDiameter = firstRow?.Size
                    ?? (feature.Attributes.Exists("Size")
                       ? feature.Attributes["Size"]
                       : null),

                    InspectedLength = firstRow?.InspectedLength
                      ?? (feature.Attributes.Exists("InspectedL")
                          ? feature.Attributes["InspectedL"]
                          : null),

                    StaType = firstRow?.StaType,
                    HighestGrade = firstRow?.HighestGrade,

                    Condition = condition,
                    TotalScore = firstRow?.TotalScore
                    ?? (feature.Attributes.Exists("STR_SCORE")
                     ? feature.Attributes["STR_SCORE"]
                     : null),

                    PeakScore = firstRow?.PeakScore
                    ?? (feature.Attributes.Exists("SER_SCORE")
                    ? feature.Attributes["SER_SCORE"]
                    : null),

                    Inspections = inspections
                });
            }

            return new { sections, summary };
        }

        public object GetSectionDetails(int id)
        {
            var result = _db.Query(@"
                SELECT 
                    s.OBJ_PK AS Id,
                    s.OBJ_Size1 AS Name,
                    s.OBJ_Spare4 AS Status,

                    si.INS_StartDate AS LastInspection,
                    so.OBS_Observation AS Observation,
                    ss.STA_TotalScore AS TotalScore,
                    ss.STA_HighestGrade AS Condition

                FROM SECTION s
                LEFT JOIN SECINSP si 
                    ON s.OBJ_PK = si.INS_Section_FK
                LEFT JOIN SECOBS so 
                    ON si.INS_PK = so.OBS_Inspection_FK
                LEFT JOIN SECSTAT ss 
                    ON si.INS_PK = ss.STA_Inspection_FK

                WHERE s.OBJ_PK = @id",
                        new { id }
                    ).FirstOrDefault();

            return new { details = result };
        }
    }
}