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

        public object ProcessGeoJson(string geoJson)
        {
            var reader = new GeoJsonReader();
            var featureCollection = reader.Read<FeatureCollection>(geoJson);

            var area = featureCollection.First().Geometry;

            var sections = _db.Query(@"
                SELECT 
                    OBJ_PK,
                    Geometry.STAsText() AS Geometry
                FROM SECTION
                WHERE Geometry.STIntersects(geometry::STGeomFromText(@area, 4326)) = 1",
                new { area = area.AsText() });

            var sectionIds = sections
                .Select(s => (int)s.OBJ_PK)
                .Distinct()
                .ToList();

            var summary = GetDynamicSectionSummary(sectionIds);

            return new
            {
                sections,
                summary
            };
        }

        public object GetDynamicSectionSummary(List<int> sectionIds)
        {
            if (sectionIds == null || sectionIds.Count == 0)
                return new List<object>();

            string idList = string.Join(",", sectionIds);

            string sql = $@"
                SELECT 
                    s.OBJ_PK AS Id,
                    s.OBJ_Size1 AS Name,
                    s.OBJ_Spare4 AS Address,

                    si.INS_SatrtDate AS LastInspection,
                    so.OBS_Observation AS Observation,
                    ss.STA_TotalScore AS TotalScore,
                    ss.STA_HighestGrade AS Condition

                FROM SECTION s
                LEFT JOIN SECINSP si 
                    ON s.OBJ_PK = si.INS_Section_FK

                LEFT JOIN SECOBS so 
                    ON si.INS_PK = so.OBS_Inspection_FK

                LEFT JOIN SECOBSMM sm 
                    ON so.OBS_PK = sm.OMM_Observation_FK

                LEFT JOIN SECSTAT ss 
                    ON si.INS_PK = ss.STA_Inspection_FK

                WHERE s.OBJ_PK IN ({idList})
            ";

            return _db.Query(sql);
        }

        public object GetSectionDetails(int id)
        {
            var result = _db.Query(@"
                SELECT 
                    s.OBJ_PK AS Id,
                    s.OBJ_Size1 AS Name,
                    s.OBJ_Spare4 AS Address,

                    si.INS_SatrtDate AS LastInspection,
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

        public object SearchInfrastructure(string query)
        {
            var q = "%" + query + "%";

            var sections = _db.Query(@"
                SELECT 
                    OBJ_PK AS Id,
                    OBJ_Size1 AS Name,
                    OBJ_Spare4 AS Address
                FROM SECTION
                WHERE OBJ_Size1 LIKE @q 
                   OR OBJ_Spare4 LIKE @q",
                new { q });

            return new
            {
                sections
            };
        }
    }
}