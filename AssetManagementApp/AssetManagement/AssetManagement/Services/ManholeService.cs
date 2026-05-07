using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetManagement.Services
{
    public class ManholeService
    {
        private readonly DataService _db;

        public ManholeService(DataService db)
        {
            _db = db;
        }

        public object GetManholeData()
        {
            var geoRows = _db.Query(@"
                SELECT
                    MGEO_PK,
                    MANID,
                    GEOMETRY_DATA.STAsText() AS GeometryWKT,
                    [DEPTH],
                    INVINL1, INLORIG1,
                    INVINL2, INLORIG2,
                    INVINL3, INLORIG3,
                    INVINL4, INLORIG4,
                    INVINL5, INLORIG5,
                    INVOUT1, OUTDEST1,
                    INVOUT2, OUTDEST2,
                    TYPE_,
                    [STATUS],
                    DRWGNUM,
                    DRWGSHT,
                    LIN,
                    MATERIAL,
                    COVER,
                    [SOURCE],
                    Comment,
                    CCTV_Depth,
                    P_COVLEVEL,
                    P_DEPTH,
                    P_INVINL1
                FROM MGEOJSON
                WHERE GEOMETRY_DATA IS NOT NULL
            ").ToList();

            if (!geoRows.Any())
                return new { summary = new List<object>() };

            // Build a lookup: MANID → geo row
            var geoByManId = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in geoRows)
            {
                string manId = Convert.ToString(g.MANID ?? "");
                if (!string.IsNullOrEmpty(manId) && !geoByManId.ContainsKey(manId))
                    geoByManId[manId] = g;
            }

            var manIds = geoByManId.Keys.ToList();
            string manIdList = string.Join(",", manIds.Select(id => $"'{id}'"));

            // NODE — material / situation / street (keyed by OBJ_Key = MANID) ──
            var nodeByManId = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            if (manIds.Any())
            {
                foreach (var n in _db.Query($@"
                    SELECT
                        OBJ_PK        AS Id,
                        OBJ_Key       AS ManId,
                        OBJ_Material  AS Material,
                        OBJ_Situation AS Situation,
                        OBJ_Street    AS Street
                    FROM NODE
                    WHERE OBJ_Key IN ({manIdList})
                ").ToList())
                {
                    string key = Convert.ToString(n.ManId ?? "");
                    if (!string.IsNullOrEmpty(key) && !nodeByManId.ContainsKey(key))
                        nodeByManId[key] = n;
                }
            }

            // NODINSP — inspections (keyed by INS_Node_FK = NODE.OBJ_PK)
            var nodePkByManId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in nodeByManId)
                nodePkByManId[kvp.Key] = Convert.ToString(kvp.Value.Id ?? "");

            var allNodePks = nodePkByManId.Values
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToList();

            var inspByNodePk = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var obsByInspId = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);
            var mediaByObsId = new Dictionary<string, List<dynamic>>(StringComparer.OrdinalIgnoreCase);

            if (allNodePks.Any())
            {
                string nodePkList = string.Join(",", allNodePks.Select(id => $"'{id}'"));

                var inspRows = _db.Query($@"
                    SELECT
                        INS_PK                AS InspectionId,
                        INS_Node_FK           AS NodePk,
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
                    WHERE INS_Node_FK IN ({nodePkList})
                ").ToList();

                foreach (var r in inspRows)
                {
                    string key = Convert.ToString(r.NodePk ?? "");
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!inspByNodePk.ContainsKey(key)) inspByNodePk[key] = new List<dynamic>();
                    inspByNodePk[key].Add(r);
                }

                var inspectionIds = inspRows
                    .Select(r => Convert.ToString(r.InspectionId ?? ""))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct()
                    .ToList();

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
                        if (!obsByInspId.ContainsKey(key)) obsByInspId[key] = new List<dynamic>();
                        obsByInspId[key].Add(o);
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
                            if (!mediaByObsId.ContainsKey(key)) mediaByObsId[key] = new List<dynamic>();
                            mediaByObsId[key].Add(m);
                        }
                    }
                }
            }

            var summary = new List<object>();

            foreach (var geo in geoRows)
            {
                string manId = Convert.ToString(geo.MANID ?? "");
                if (string.IsNullOrEmpty(manId)) continue;

                string geometryWkt = Convert.ToString(geo.GeometryWKT ?? "");
                if (string.IsNullOrEmpty(geometryWkt)) continue;

                nodeByManId.TryGetValue(manId, out var node);
                string nodePk = node != null ? Convert.ToString(node.Id ?? "") : "";

                // Inspections for this node
                inspByNodePk.TryGetValue(nodePk, out var rawInsp);
                var orderedInsp = (rawInsp ?? new List<dynamic>())
                    .Where(r => r.InspectionId != null)
                    .GroupBy(r => Convert.ToString(r.InspectionId ?? ""))
                    .Select(g => g.First())
                    .OrderBy(r => r.InspectionDate)
                    .ToList();

                var lastInsp = orderedInsp.LastOrDefault();

                // Build inspection detail list
                var inspectionDetails = new List<object>();
                int inspNum = 1;
                foreach (var insp in orderedInsp)
                {
                    string insId = Convert.ToString(insp.InspectionId ?? "");

                    obsByInspId.TryGetValue(insId, out var rawObs);
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
                            mediaByObsId.TryGetValue(obsId, out var obsMedia);
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

                // Inlets / outlets from MGEOJSON
                var inlets = new[]
                {
                    new { InvLevel = TryDouble(geo.INVINL1), Origin = Convert.ToString(geo.INLORIG1 ?? "") },
                    new { InvLevel = TryDouble(geo.INVINL2), Origin = Convert.ToString(geo.INLORIG2 ?? "") },
                    new { InvLevel = TryDouble(geo.INVINL3), Origin = Convert.ToString(geo.INLORIG3 ?? "") },
                    new { InvLevel = TryDouble(geo.INVINL4), Origin = Convert.ToString(geo.INLORIG4 ?? "") },
                    new { InvLevel = TryDouble(geo.INVINL5), Origin = Convert.ToString(geo.INLORIG5 ?? "") },
                }.Where(x => x.InvLevel != null || !string.IsNullOrEmpty(x.Origin)).ToList();

                var outlets = new[]
                {
                    new { InvLevel = TryDouble(geo.INVOUT1), Dest = Convert.ToString(geo.OUTDEST1 ?? "") },
                    new { InvLevel = TryDouble(geo.INVOUT2), Dest = Convert.ToString(geo.OUTDEST2 ?? "") },
                }.Where(x => x.InvLevel != null || !string.IsNullOrEmpty(x.Dest)).ToList();

                summary.Add(new
                {
                    Id = manId,
                    Name = manId,
                    Geometry = geometryWkt,
                    Material = node != null? Convert.ToString(node.Material ?? ""): Convert.ToString(geo.MATERIAL ?? ""),
                    Street = node != null ? Convert.ToString(node.Street ?? "") : "",
                    Situation = node != null ? Convert.ToString(node.Situation ?? "") : "",
                    Depth = TryDouble(geo.DEPTH),
                    CctvDepth = TryDouble(geo.CCTV_Depth),
                    Type = Convert.ToString(geo.TYPE_ ?? ""),
                    Status = Convert.ToString(geo.STATUS ?? ""),
                    Cover = Convert.ToString(geo.COVER ?? ""),
                    Source = Convert.ToString(geo.SOURCE ?? ""),
                    Problem = Convert.ToString(geo.PROBLEM ?? ""),
                    Comment = Convert.ToString(geo.Comment ?? ""),
                    DrawingNumber = Convert.ToString(geo.DRWGNUM ?? ""),
                    DrawingSheet = Convert.ToString(geo.DRWGSHT ?? ""),
                    Lin = Convert.ToString(geo.LIN ?? ""),
                    CoverLevel = TryDouble(geo.P_COVLEVEL),
                    PDepth = TryDouble(geo.P_DEPTH),
                    PInvInl1 = TryDouble(geo.P_INVINL1),
                    Inlets = inlets,
                    Outlets = outlets,
                    LastInspection = lastInsp?.InspectionDate,
                    InspectedLength = lastInsp?.InspectedLength,
                    Inspected = isInspected ? "Inspected" : "Uninspected",
                    Inspections = inspectionDetails
                });
            }

            return new { summary };
        }

        private static double? TryDouble(object val)
        {
            if (val == null) return null;
            return double.TryParse(Convert.ToString(val), out double d) ? d : (double?)null;
        }
    }
}