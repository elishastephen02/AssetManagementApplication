namespace AssetManagement.Services
{
    public class ManholeService
    {
        private readonly DataService _db;

        public ManholeService(DataService db)
        {
            _db = db;
        }

        public object GetManholeMapData()
        {
            // ── 1. Manholes (NODE) ────────────────────────────────────────────────
            var nodes = _db.Query(@"
                SELECT
                    OBJ_PK         AS Id,
                    OBJ_Key        AS Name,
                    OBJ_Material   AS Material,
                    OBJ_Situation  AS Situation,
                    OBJ_Street     AS Street
                FROM NODE
            ").ToList();

            var nodeIds = nodes
                .Select(n => Convert.ToString(n.Id ?? ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();

            if (!nodeIds.Any())
                return new { nodes = new List<object>(), summary = new List<object>() };

            string nodeIdList = string.Join(",", nodeIds.Select(id => $"'{id}'"));

            // ── 2. Inspections (NODINSP) ──────────────────────────────────────────
            var inspRows = _db.Query($@"
                SELECT
                    INS_PK                AS InspectionId,
                    INS_Node_FK           AS NodeId,
                    INS_Job_FK            AS JobId,
                    INS_StartDate         AS InspectionDate,
                    INS_Method            AS Method,
                    INS_Drainage          AS Drainage,
                    INS_InspectionDir     AS InspectionDir,
                    INS_Equipment_REF     AS Equipment,
                    INS_InspectedLength   AS InspectedLength,
                    INS_PhotoMedia        AS PhotoMedia,
                    INS_Spare3            AS Spare3,
                    INS_Spare8            AS Spare8
                FROM NODINSP
                WHERE INS_Node_FK IN ({nodeIdList})
            ").ToList();

            // inspections keyed by NodeId
            var inspByNode = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in inspRows)
            {
                string key = Convert.ToString(r.NodeId ?? "");
                if (string.IsNullOrEmpty(key)) continue;
                if (!inspByNode.ContainsKey(key)) inspByNode[key] = new List<dynamic>();
                inspByNode[key].Add(r);
            }

            var inspectionIds = inspRows
                .Select(r => Convert.ToString(r.InspectionId ?? ""))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();

            // ── 3. Observations (NODOBS) ──────────────────────────────────────────
            var obsByInsp = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var mediaByObs = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            if (inspectionIds.Any())
            {
                string insIdList = string.Join(",", inspectionIds.Select(id => $"'{id}'"));

                var observations = _db.Query($@"
                    SELECT
                        OBS_PK             AS ObsId,
                        OBS_Inspection_FK  AS InspectionId,
                        OBS_Depth          AS Depth,
                        OBS_DepthToGo      AS DepthToGo,
                        OBS_Observation    AS Observation,
                        OBS_O3_Value       AS O3Value
                    FROM NODOBS
                    WHERE OBS_Inspection_FK IN ({insIdList})
                ").ToList();

                foreach (var o in observations)
                {
                    string key = Convert.ToString(o.InspectionId ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!obsByInsp.ContainsKey(key)) obsByInsp[key] = new List<dynamic>();
                    obsByInsp[key].Add(o);
                }

                // ── 4. Media (NODOBSMM) ───────────────────────────────────────────
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
                            OMM_Observation_FK  AS ObsId,
                            OMM_Type            AS MediaType,
                            OMM_FileName        AS FileName,
                            OMM_FileType        AS FileType
                        FROM NODOBSMM
                        WHERE OMM_Observation_FK IN ({obsIdList})
                    ").ToList())
                    {
                        string key = Convert.ToString(m.ObsId ?? "");
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!mediaByObs.ContainsKey(key)) mediaByObs[key] = new List<dynamic>();
                        mediaByObs[key].Add(m);
                    }
                }
            }

            // ── 5. Assemble response ──────────────────────────────────────────────
            var summaryList = new List<object>();
            var nodeGeoList = new List<object>();   // placeholder for geometry if added later

            foreach (var node in nodes)
            {
                string nodeId = Convert.ToString(node.Id ?? "");
                if (string.IsNullOrEmpty(nodeId)) continue;

                inspByNode.TryGetValue(nodeId, out var nodeInspections);

                var orderedInsp = (nodeInspections ?? new List<dynamic>())
                    .Where(r => r.InspectionId != null)
                    .GroupBy(r => Convert.ToString(r.InspectionId ?? ""))
                    .Select(g => g.First())
                    .OrderBy(r => r.InspectionDate)
                    .ToList();

                var lastInsp = orderedInsp.LastOrDefault();

                // Build inspection detail objects
                var inspectionDetails = new List<object>();
                int inspNum = 1;
                foreach (var insp in orderedInsp)
                {
                    string insId = Convert.ToString(insp.InspectionId ?? "");

                    obsByInsp.TryGetValue(insId, out var rawObs);
                    var obsWithMedia = (rawObs ?? new List<dynamic>())
                        .OrderBy(o =>
                        {
                            decimal d;
                            return decimal.TryParse(Convert.ToString(o.Depth ?? ""), out d)
                                ? d : decimal.MaxValue;
                        })
                        .Select(o =>
                        {
                            string obsId = Convert.ToString(o.ObsId ?? "");
                            mediaByObs.TryGetValue(obsId, out var obsMedia);
                            return new
                            {
                                o.Depth,
                                o.DepthToGo,
                                o.Observation,
                                o.O3Value,
                                Media = (obsMedia ?? new List<dynamic>())
                                    .Select(m => new { m.FileName, m.FileType, m.MediaType })
                                    .ToList()
                            };
                        })
                        .ToList();

                    inspectionDetails.Add(new
                    {
                        InspectionNumber = inspNum++,
                        InspectionDate = insp.InspectionDate,
                        Method = insp.Method,
                        Drainage = insp.Drainage,
                        InspectionDir = insp.InspectionDir,
                        Equipment = insp.Equipment,
                        InspectedLength = insp.InspectedLength,
                        PhotoMedia = insp.PhotoMedia,
                        Observations = obsWithMedia
                    });
                }

                bool isInspected = orderedInsp.Any();

                summaryList.Add(new
                {
                    Id = nodeId,
                    Name = Convert.ToString(node.Name ?? nodeId),
                    Material = Convert.ToString(node.Material ?? ""),
                    Street = Convert.ToString(node.Street ?? ""),
                    Situation = Convert.ToString(node.Situation ?? ""),
                    LastInspection = lastInsp?.InspectionDate,
                    InspectedLength = lastInsp?.InspectedLength,
                    Inspected = isInspected ? "Inspected" : "Uninspected",
                    Inspections = inspectionDetails
                });
            }

            return new { summary = summaryList };
        }
    }
}
