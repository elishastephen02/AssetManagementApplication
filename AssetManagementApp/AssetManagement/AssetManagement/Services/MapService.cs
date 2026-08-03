using Microsoft.Data.SqlClient;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using static System.Collections.Specialized.BitVector32;

namespace AssetManagement.Services
{
    public class MapService
    {
        private readonly DataService _db;

        public MapService(DataService db)
        {
            _db = db;
        }

        public object GetMapData(double north, double south, double east, double west)
        {
            string bbox =
                $"POLYGON((" +
                $"{west} {north}," +
                $"{east} {north}," +
                $"{east} {south}," +
                $"{west} {south}," +
                $"{west} {north}))";

            var geoRows = _db.Query(@"
                DECLARE @bboxGeom geometry =
                geometry::STGeomFromText(@bbox,4326);

                SELECT
                    SEGID,
                    RoadName,
                    MATERIAL,
                    InspectedL AS InspectedLength,
                    InspectedD AS InspectedDate,
                    X,
                    Y,
                    DESDATE,
                    STR_SCORE,
                    STR_GRADE,
                    SER_SCORE,
                    SER_GRADE,
                    Expected,
                    AGE,
                    ConditionR,
                    RemainingUL,
                    DisposalD,
                    Impairment,
                    CurrentRC,
                    DepreciatedRV,
                    GEOMETRY_DATA.Reduce(0.00001).STAsText() AS GeometryWKT
                FROM GEOJSON
                WHERE
                    GEOMETRY_DATA.Filter(@bboxGeom)=1
                AND
                    GEOMETRY_DATA.STIntersects(@bboxGeom)=1;
                ", new { bbox }).ToList();

            var segIds = geoRows
            .Select(g => Convert.ToString(g.SEGID))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

            // Nothing in this viewport — bail out before building an invalid
            // "IN ()" clause below.
            if (!segIds.Any())
            {
                return new { sections = new List<object>(), summary = new List<object>() };
            }

            string segIdsCsv = string.Join(",", segIds);

            var dbSections = _db.Query(@"
                SELECT
                    s.OBJ_PK AS Id,
                    s.OBJ_Key AS SegId,
                    s.OBJ_Size1 AS Size,
                    s.OBJ_Material AS Material,
                    s.OBJ_Spare4 AS Status,
                    s.OBJ_Spare5 AS Owner,
                    si.INS_PK AS InspectionId,
                    si.INS_StartDate AS InspectionDate,
                    si.INS_InspectedLength AS InspectedLength
                FROM SECTION s
                LEFT JOIN SECINSP si
                    ON s.OBJ_PK_Key = si.INS_Section_FK_Key
                WHERE s.OBJ_Key_Key IN (SELECT value FROM STRING_SPLIT(@segIdsCsv, ','))
            ", new { segIdsCsv }).ToList();

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

            string insIdsCsv = inspectionIds.Any() ? string.Join(",", inspectionIds) : null;

            // Filtered to just the inspections in view — this used to scan
            // the entire SECSTAT table on every single viewport load.
            var statsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            if (insIdsCsv != null)
            {
                foreach (var st in _db.Query(@"
                    SELECT
                        STA_Inspection_FK AS InspectionId,
                        STA_HighestGrade  AS HighestGrade,
                        STA_TotalScore    AS TotalScore,
                        STA_PeakScore     AS PeakScore
                    FROM SECSTAT
                    WHERE STA_Type = 'STR'
                    AND STA_Inspection_FK_Key IN (SELECT value FROM STRING_SPLIT(@insIdsCsv, ','))
                ", new { insIdsCsv }).ToList())
                {
                    string key = Convert.ToString(st.InspectionId ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!statsLookup.ContainsKey(key)) statsLookup[key] = new List<dynamic>();
                    statsLookup[key].Add(st);
                }
            }

            var observationsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var mediaLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            if (inspectionIds.Any())
            {
                var observations = _db.Query(@"
                    SELECT
                        OBS_PK            AS ObsId,
                        OBS_Inspection_FK AS InspectionId,
                        OBS_Distance      AS Distance,
                        OBS_Observation   AS Observation,
                        OBS_GradeS        AS Grade,
                        OBS_ScoreS        AS Score
                    FROM SECOBS
                    WHERE OBS_Inspection_FK_Key IN (SELECT value FROM STRING_SPLIT(@insIdsCsv, ','))
                ", new { insIdsCsv }).ToList();

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
                    string obsIdsCsv = string.Join(",", obsIds);

                    foreach (var m in _db.Query(@"
                        SELECT
                            OMM_Observation_FK AS ObsId,
                            OMM_FileName       AS FileName,
                            OMM_FileType       AS FileType
                        FROM SECOBSMM
                        WHERE OMM_Observation_FK_Key IN (SELECT value FROM STRING_SPLIT(@obsIdsCsv, ','))
                    ", new { obsIdsCsv }).ToList())
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
                    SER_GRADE = geo.SER_GRADE,
                    DesDate = geo.DESDATE,
                    XCoord = geo.X,
                    YCoord = geo.Y,
                    Expected = geo.Expected,
                    Age = geo.AGE,
                    ConditionR = geo.ConditionR,
                    RemainingUL = geo.RemainingUL,
                    DisposalDate = geo.DisposalD,
                    Impairment = geo.Impairment,
                    CurrentReplacementCost = geo.CurrentRC,
                    DepreciatedReplacementValue = geo.DepreciatedRV
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

        // SEARCH — deliberately queries the whole GEOJSON table with no
        // bounding-box / viewport filter, so it can find any pipe in the
        // database regardless of what's currently on screen. Results are
        // ranked so the closest match to the typed term comes first,
        // instead of a flat alphabetical-by-road ordering that could bury
        // an exact pipe ID or road match past the row cap below.
        public IEnumerable<object> SearchPipes(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<object>();

            string trimmed = searchTerm.Trim();
            string search = $"%{trimmed}%";

            // Raised from 50 -> 200: with only 50 rows, a broad term (e.g. a
            // material name or common road) could silently truncate results
            // to whatever sorted alphabetically first, which looked like
            // "search only finds nearby pipes" even though nothing was
            // actually viewport-scoped.
            var results = _db.Query(@"
                SELECT TOP (200)

                    SEGID,
                    RoadName,
                    MATERIAL,
                    X,
                    Y,
                    GEOMETRY_DATA.Reduce(0.00001).STAsText() AS GeometryWKT

                FROM GEOJSON

                WHERE
                       SEGID LIKE @search
                    OR RoadName LIKE @search
                    OR MATERIAL LIKE @search

                ORDER BY
                    -- exact pipe ID match first, then exact road match,
                    -- then prefix matches, then everything else alphabetically
                    CASE WHEN SEGID = @trimmed THEN 0
                         WHEN RoadName = @trimmed THEN 1
                         WHEN SEGID LIKE @trimmed + '%' THEN 2
                         WHEN RoadName LIKE @trimmed + '%' THEN 3
                         ELSE 4
                    END,
                    RoadName,
                    SEGID
            ", new { search, trimmed });

            return results.Select(r => new
            {
                Id = Convert.ToString(r.SEGID),
                Name = Convert.ToString(r.SEGID),
                RoadName = Convert.ToString(r.RoadName),
                Material = Convert.ToString(r.MATERIAL),
                X = r.X,
                Y = r.Y,
                Geometry = Convert.ToString(r.GeometryWKT)
            });
        }

        public object? GetPipe(string segId)
        {
            if (string.IsNullOrWhiteSpace(segId))
                return null;

            var pipe = _db.Query(@"
                SELECT TOP (1)
                    SEGID, RoadName, MATERIAL,
                    InspectedL AS InspectedLength, InspectedD AS InspectedDate,
                    X, Y, DESDATE, STR_SCORE, STR_GRADE, SER_SCORE, SER_GRADE,
                    Expected, AGE, ConditionR, RemainingUL, DisposalD,
                    Impairment, CurrentRC, DepreciatedRV,
                    GEOMETRY_DATA.Reduce(0.00001).STAsText() AS GeometryWKT
                FROM GEOJSON
                WHERE SEGID = @segId
            ", new { segId }).FirstOrDefault();

            if (pipe == null)
                return null;

            // --- NEW: pull inspections/observations for this one pipe ---
            var dbRows = _db.Query(@"
                SELECT
                    s.OBJ_PK AS Id,
                    s.OBJ_Key AS SegId,
                    s.OBJ_Size1 AS Size,
                    s.OBJ_Material AS Material,
                    si.INS_PK AS InspectionId,
                    si.INS_StartDate AS InspectionDate,
                    si.INS_InspectedLength AS InspectedLength
                FROM SECTION s
                LEFT JOIN SECINSP si
                    ON s.OBJ_PK_Key = si.INS_Section_FK_Key
                WHERE s.OBJ_Key_Key = @segId
            ", new { segId }).ToList();

            var distinctInspections = dbRows
                .Where(r => r.InspectionId != null)
                .GroupBy(r => Convert.ToString(r.InspectionId ?? ""))
                .Select(g => g.First())
                .OrderBy(r => r.InspectionDate)
                .ToList();

            var inspectionIds = distinctInspections
                .Select(r => Convert.ToString(r.InspectionId ?? ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            var statsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var observationsLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var mediaLookup = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            if (inspectionIds.Any())
            {
                string insIdsCsv = string.Join(",", inspectionIds);

                foreach (var st in _db.Query(@"
                    SELECT STA_Inspection_FK AS InspectionId, STA_HighestGrade AS HighestGrade,
                           STA_TotalScore AS TotalScore, STA_PeakScore AS PeakScore
                    FROM SECSTAT
                    WHERE STA_Type = 'STR'
                      AND STA_Inspection_FK_Key IN (SELECT value FROM STRING_SPLIT(@insIdsCsv, ','))
                ", new { insIdsCsv }).ToList())
                {
                    string key = Convert.ToString(st.InspectionId ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!statsLookup.ContainsKey(key)) statsLookup[key] = new List<dynamic>();
                    statsLookup[key].Add(st);
                }

                var observations = _db.Query(@"
                    SELECT OBS_PK AS ObsId, OBS_Inspection_FK AS InspectionId,
                           OBS_Distance AS Distance, OBS_Observation AS Observation,
                           OBS_GradeS AS Grade, OBS_ScoreS AS Score
                    FROM SECOBS
                    WHERE OBS_Inspection_FK_Key IN (SELECT value FROM STRING_SPLIT(@insIdsCsv, ','))
                ", new { insIdsCsv }).ToList();

                foreach (var o in observations)
                {
                    string key = Convert.ToString(o.InspectionId ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!observationsLookup.ContainsKey(key)) observationsLookup[key] = new List<dynamic>();
                    observationsLookup[key].Add(o);
                }

                var obsIds = observations.Select(o => Convert.ToString(o.ObsId ?? ""))
                                          .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();

                if (obsIds.Any())
                {
                    string obsIdsCsv = string.Join(",", obsIds);
                    foreach (var m in _db.Query(@"
                        SELECT OMM_Observation_FK AS ObsId, OMM_FileName AS FileName, OMM_FileType AS FileType
                        FROM SECOBSMM
                        WHERE OMM_Observation_FK_Key IN (SELECT value FROM STRING_SPLIT(@obsIdsCsv, ','))
                    ", new { obsIdsCsv }).ToList())
                    {
                        string key = Convert.ToString(m.ObsId ?? "");
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!mediaLookup.ContainsKey(key)) mediaLookup[key] = new List<dynamic>();
                        mediaLookup[key].Add(m);
                    }
                }
            }

            var inspections = new List<object>();
            int inspNum = 1;
            foreach (var insp in distinctInspections)
            {
                string insId = Convert.ToString(insp.InspectionId ?? "");
                statsLookup.TryGetValue(insId, out var statRows);
                var stats = (statRows ?? new List<dynamic>())
                    .Select(st => new { HighestGrade = st.HighestGrade, TotalScore = st.TotalScore, PeakScore = st.PeakScore })
                    .ToList();

                observationsLookup.TryGetValue(insId, out var obsList);
                var obsWithMedia = (obsList ?? new List<dynamic>())
                    .OrderBy(o => { decimal d; return decimal.TryParse(Convert.ToString(o.Distance ?? ""), out d) ? d : decimal.MaxValue; })
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
                            Media = (obsMedia ?? new List<dynamic>()).Select(m => new { m.FileName, m.FileType }).ToList()
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

            var lastInsp = distinctInspections.LastOrDefault();
            var firstRow = dbRows.Where(r => r.InspectionId != null).OrderBy(r => r.InspectionDate).FirstOrDefault();

            return new
            {
                OBJ_PK = Convert.ToString(pipe.SEGID),
                Geometry = Convert.ToString(pipe.GeometryWKT),
                STR_SCORE = pipe.STR_SCORE,
                STR_GRADE = pipe.STR_GRADE,
                SER_SCORE = pipe.SER_SCORE,
                SER_GRADE = pipe.SER_GRADE,
                DesDate = pipe.DESDATE,
                XCoord = pipe.X,
                YCoord = pipe.Y,
                Expected = pipe.Expected,
                Age = pipe.AGE,
                ConditionR = pipe.ConditionR,
                RemainingUL = pipe.RemainingUL,
                DisposalDate = pipe.DisposalD,
                Impairment = pipe.Impairment,
                CurrentReplacementCost = pipe.CurrentRC,
                DepreciatedReplacementValue = pipe.DepreciatedRV,
                Id = Convert.ToString(pipe.SEGID),
                Name = Convert.ToString(pipe.SEGID),
                Material = firstRow?.Material != null ? Convert.ToString(firstRow.Material) : Convert.ToString(pipe.MATERIAL),
                PipeDiameter = firstRow?.Size,
                Address = Convert.ToString(pipe.RoadName),
                LastInspection = lastInsp?.InspectionDate ?? pipe.InspectedDate,
                InspectedLength = lastInsp?.InspectedLength ?? pipe.InspectedLength,
                Inspected = distinctInspections.Any() ? "Inspected" : "Uninspected",
                Condition = GetPipeCondition(pipe.STR_GRADE),
                Inspections = inspections
            };
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