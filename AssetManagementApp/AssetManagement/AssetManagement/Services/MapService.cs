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
                    STR_GRADE,
                    SER_SCORE,
                    SER_GRADE,
                    GEOMETRY_DATA.STAsText() AS GeometryWKT
                FROM GEOJSON
                WHERE GEOMETRY_DATA IS NOT NULL
            ").ToList();

            // Sections — no SECSTAT join here, kept flat
            var dbSections = _db.Query(@"
                SELECT
                    s.OBJ_PK           AS Id,
                    s.OBJ_Key          AS SegId,
                    s.OBJ_Size1        AS Size,
                    s.OBJ_Material     AS Material,
                    s.OBJ_Spare4       AS Status,
                    s.OBJ_Spare5       AS Owner,
                    si.INS_PK          AS InspectionId,
                    si.INS_StartDate   AS InspectionDate,
                    si.INS_InspectedLength AS InspectedLength
                FROM SECTION s
                LEFT JOIN SECINSP si ON s.OBJ_PK = si.INS_Section_FK
            ").ToList();

            // SECSTAT — fetched separately, grouped by inspection
            var allStats = _db.Query(@"
                SELECT
                    STA_Inspection_FK AS InspectionId,
                    STA_HighestGrade  AS HighestGrade,
                    STA_TotalScore    AS TotalScore,
                    STA_PeakScore     AS PeakScore
                FROM SECSTAT
            ").ToList();

            // Group stats by InspectionId
            var statsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in allStats)
            {
                string key = Convert.ToString(st.InspectionId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!statsLookup.ContainsKey(key)) statsLookup[key] = new List<dynamic>();
                statsLookup[key].Add(st);
            }

            // Group sections by SegId
            var dbLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in dbSections)
            {
                string key = Convert.ToString(row.SegId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!dbLookup.ContainsKey(key)) dbLookup[key] = new List<dynamic>();
                dbLookup[key].Add(row);
            }

            // Collect all unique inspection IDs for observation query
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

                // Pass GeoJSON score fields through to the sections list
                sections.Add(new
                {
                    OBJ_PK = segId,
                    Geometry = (string)geo.GeometryWKT,
                    STR_SCORE = geo.STR_SCORE,
                    STR_GRADE = geo.STR_GRADE,
                    SER_SCORE = geo.SER_SCORE,
                    SER_GRADE = geo.SER_GRADE
                });

                dbLookup.TryGetValue(segId, out var dbRows);

                // Deduplicate inspections — one row per INS_PK
                var distinctInspections = dbRows?
                    .Where(r => r.InspectionId != null)
                    .GroupBy(r => Convert.ToString(r.InspectionId ?? ""))
                    .Select(g => g.First())
                    .OrderBy(r => r.InspectionDate)
                    .ToList();

                var firstRow = distinctInspections?.FirstOrDefault();

                // Build inspections list — each with its own Stats array from SECSTAT
                var inspections = new List<object>();
                if (distinctInspections != null)
                {
                    int inspNum = 1;
                    foreach (var insp in distinctInspections)
                    {
                        string insId = Convert.ToString(insp.InspectionId ?? "");

                        // SECSTAT rows for this inspection (the two records)
                        statsLookup.TryGetValue(insId, out var statRows);
                        var stats = (statRows ?? new List<dynamic>()).Select(st => new
                        {
                            HighestGrade = st.HighestGrade,
                            TotalScore = st.TotalScore,
                            PeakScore = st.PeakScore
                        }).ToList();

                        // Observations for this inspection
                        var pipeObs = !string.IsNullOrEmpty(insId)
                            ? observations
                                .Where(o => Convert.ToString(o.InspectionId ?? "") == insId)
                                .OrderBy(o => {
                                    decimal d;
                                    return decimal.TryParse(Convert.ToString(o.Distance ?? ""), out d)
                                        ? d : decimal.MaxValue;
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
                            InspectionDate = insp.InspectionDate,
                            InspectedLength = insp.InspectedLength,
                            Stats = stats,          // ← both SECSTAT rows
                            Observations = obsWithMedia
                        });
                    }
                }

                bool hasInspection = distinctInspections?.Any() == true;

                // Use first stat record's HighestGrade as the summary grade
                dynamic firstStat = null;
                if (distinctInspections != null && distinctInspections.Any())
                {
                    string lastInsId = Convert.ToString(distinctInspections.Last().InspectionId ?? "");
                    if (statsLookup.TryGetValue(lastInsId, out List<dynamic> lastStats) && lastStats.Any())
                    {
                        firstStat = lastStats.First();
                    }
                }

                summary.Add(new
                {
                    Id = segId,
                    Name = segId,
                    Material = firstRow?.Material != null
                                        ? Convert.ToString(firstRow.Material)
                                        : Convert.ToString(geo.MATERIAL ?? ""),
                    Address = Convert.ToString(geo.RoadName ?? ""),
                    PipeDiameter = firstRow?.Size,
                    LastInspection = distinctInspections?.LastOrDefault()?.InspectionDate
                                        ?? geo.InspectedDate,
                    InspectedLength = distinctInspections?.LastOrDefault()?.InspectedLength
                                        ?? geo.InspectedLength,
                    HighestGrade = firstStat?.HighestGrade,
                    TotalScore = firstStat?.TotalScore ?? geo.STR_SCORE,
                    PeakScore = firstStat?.PeakScore ?? geo.SER_SCORE,
                    Inspected = hasInspection ? "Inspected" : "Uninspected",
                    Condition = GetPipeCondition(firstStat?.HighestGrade ?? geo.STR_GRADE),
                    Inspections = inspections
                });
            }

            return new { sections, summary };
        }

        private string GetPipeCondition(object scoreObj)
        {
            if (scoreObj == null)
                return "N/A";

            if (!int.TryParse(scoreObj.ToString(), out int score))
                return "N/A";

            return score switch
            {
                1 or 2 => "Good",
                3 => "Okay",
                4 or 5 => "Bad",
                _ => "N/A"
            };
        }
    }
}