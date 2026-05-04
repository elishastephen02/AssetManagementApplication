using NetTopologySuite.Features;
using NetTopologySuite.IO;

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

            var statsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var st in _db.Query(@"
                SELECT
                    STA_Inspection_FK AS InspectionId,
                    STA_HighestGrade  AS HighestGrade,
                    STA_TotalScore    AS TotalScore,
                    STA_PeakScore     AS PeakScore
                FROM SECSTAT
                WHERE STA_Type = 'STR'
            ").ToList())
            {
                string key = Convert.ToString(st.InspectionId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!statsLookup.ContainsKey(key)) statsLookup[key] = new List<dynamic>();
                statsLookup[key].Add(st);
            }

            var dbLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in dbSections)
            {
                string key = Convert.ToString(r.SegId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!dbLookup.ContainsKey(key)) dbLookup[key] = new List<dynamic>();
                dbLookup[key].Add(r);
            }

            var inspectionIds = dbSections
                .Select(r => Convert.ToString(r.InspectionId ?? ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();

            var observationsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var mediaLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            if (inspectionIds.Any())
            {
                string insIdList = string.Join(",", inspectionIds.Select(id => $"'{id}'"));

                var observations = _db.Query($@"
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

                foreach (var o in observations)
                {
                    string key = Convert.ToString(o.InspectionId ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!observationsLookup.ContainsKey(key)) observationsLookup[key] = new List<dynamic>();
                    observationsLookup[key].Add(o);
                }

                var obsIds = observations
                    .Select(o => Convert.ToString(o.ObsId ?? ""))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct()
                    .ToList();

                if (obsIds.Any())
                {
                    string obsIdList = string.Join(",", obsIds.Select(id => $"'{id}'"));

                    foreach (var m in _db.Query($@"
                        SELECT
                            OMM_Observation_FK AS ObsId,
                            OMM_FileName       AS FileName,
                            OMM_FileType       AS FileType
                        FROM SECOBSMM
                        WHERE OMM_Observation_FK IN ({obsIdList})
                    ").ToList())
                    {
                        string key = Convert.ToString(m.ObsId ?? "");
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!mediaLookup.ContainsKey(key)) mediaLookup[key] = new List<dynamic>();
                        mediaLookup[key].Add(m);
                    }
                }
            }

            var sections = new List<object>();
            var summary = new List<object>();

            foreach (var geo in geoRows)
            {
                string segId = Convert.ToString(geo.SEGID ?? "");
                if (string.IsNullOrEmpty(segId)) continue;

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

                var distinctInspections = dbRows?
                    .Where(r => r.InspectionId != null)
                    .GroupBy(r => Convert.ToString(r.InspectionId ?? ""))
                    .Select(g => g.First())
                    .OrderBy(r => r.InspectionDate)
                    .ToList();

                var firstRow = distinctInspections?.FirstOrDefault();
                var lastInsp = distinctInspections?.LastOrDefault();

                dynamic lastStat = null;
                if (lastInsp != null)
                {
                    string lastInsId = Convert.ToString(lastInsp.InspectionId ?? "");
                    if (statsLookup.TryGetValue(lastInsId, out var lastStats) && lastStats.Any())
                        lastStat = lastStats.First();
                }

                var inspections = new List<object>();
                if (distinctInspections != null)
                {
                    int inspNum = 1;
                    foreach (var insp in distinctInspections)
                    {
                        string insId = Convert.ToString(insp.InspectionId ?? "");

                        statsLookup.TryGetValue(insId, out var statRows);
                        var stats = (statRows ?? new List<dynamic>())
                            .Select(st => new
                            {
                                HighestGrade = st.HighestGrade,
                                TotalScore = st.TotalScore,
                                PeakScore = st.PeakScore
                            })
                            .ToList();

                        observationsLookup.TryGetValue(insId, out var pipeObsList);
                        var obsWithMedia = (pipeObsList ?? new List<dynamic>())
                            .OrderBy(o =>
                            {
                                decimal d;
                                return decimal.TryParse(Convert.ToString(o.Distance ?? ""), out d)
                                    ? d : decimal.MaxValue;
                            })
                            .Select(o =>
                            {
                                string obsId = Convert.ToString(o.ObsId ?? "");
                                mediaLookup.TryGetValue(obsId, out var obsMedia);
                                return new
                                {
                                    o.Distance,
                                    o.Observation,
                                    o.Grade,
                                    o.Score,
                                    Media = (obsMedia ?? new List<dynamic>())
                                        .Select(m => new { m.FileName, m.FileType })
                                        .ToList()
                                };
                            })
                            .ToList();

                        inspections.Add(new
                        {
                            InspectionNumber = inspNum++,
                            InspectionDate = insp.InspectionDate,
                            InspectedLength = insp.InspectedLength,
                            Stats = stats,
                            Observations = obsWithMedia
                        });
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
                    LastInspection = lastInsp?.InspectionDate ?? geo.InspectedDate,
                    InspectedLength = lastInsp?.InspectedLength ?? geo.InspectedLength,
                    HighestGrade = lastStat?.HighestGrade,
                    TotalScore = lastStat?.TotalScore ?? geo.STR_SCORE,
                    PeakScore = lastStat?.PeakScore ?? geo.SER_SCORE,
                    Inspected = distinctInspections?.Any() == true ? "Inspected" : "Uninspected",
                    Condition = GetPipeCondition(lastStat?.HighestGrade ?? geo.STR_GRADE),
                    Inspections = inspections
                });
            }

            return new { sections, summary };
        }

        private string GetPipeCondition(object scoreObj)
        {
            if (scoreObj == null) return "N/A";
            if (!int.TryParse(scoreObj.ToString(), out int score)) return "N/A";

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