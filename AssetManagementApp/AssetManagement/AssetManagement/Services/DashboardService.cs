using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace AssetManagement.Services
{
    public class DashboardService
    {
        private readonly DataService _db;

        public DashboardService(DataService db)
        {
            _db = db;
        }

        public object GetDashboardGeoJson()
        {
            var sql = @"
                SELECT
                    b.BlockName,
                    b.Geometry.STAsText() AS GeomWkt,
                    a.ConditionR
                FROM Blocks b
                LEFT JOIN
                (
                    SELECT
                        BlockName,
                        AVG(TRY_CAST(ConditionR AS DECIMAL(10,2))) AS ConditionR
                    FROM GEOJSON
                    WHERE ConditionR IS NOT NULL
                    GROUP BY BlockName
                ) a
                    ON UPPER(TRIM(a.BlockName)) = UPPER(TRIM(b.BlockName))
                    OR UPPER(TRIM(a.BlockName)) LIKE UPPER(TRIM(b.BlockName)) + ' %'
                WHERE b.Geometry IS NOT NULL;
            ";

            var rows = _db.Query(sql);

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var reader = new WKTReader(factory);

            var features = new List<object>();

            foreach (var row in rows)
            {
                if (row.GeomWkt == null)
                    continue;

                try
                {
                    string wkt = (string)row.GeomWkt;

                    if (wkt.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
                        wkt = wkt[(wkt.IndexOf(';') + 1)..];

                    var geometry = reader.Read(wkt);

                    var geoJson = GeometryToGeoJsonObject(geometry);

                    if (geoJson == null)
                        continue;

                    double? condition = null;

                    if (row.ConditionR != null)
                        condition = Convert.ToDouble(row.ConditionR);

                    features.Add(new
                    {
                        type = "Feature",
                        properties = new
                        {
                            blockName = (string)row.BlockName,
                            condition = condition ?? 0,
                            hasConditionData = condition.HasValue
                        },
                        geometry = geoJson
                    });
                }
                catch
                {
                    // Ignore bad geometry and continue
                }
            }

            return new
            {
                type = "FeatureCollection",
                features
            };
        }

        private static object? GeometryToGeoJsonObject(Geometry geom)
        {
            switch (geom)
            {
                case Polygon poly:
                    return new
                    {
                        type = "Polygon",
                        coordinates = PolygonToCoords(poly)
                    };

                case MultiPolygon mp:
                    return new
                    {
                        type = "MultiPolygon",
                        coordinates = Enumerable.Range(0, mp.NumGeometries)
                            .Select(i => PolygonToCoords((Polygon)mp.GetGeometryN(i)))
                            .ToArray()
                    };

                default:
                    return null;
            }
        }

        private static double[][][] PolygonToCoords(Polygon poly)
        {
            var rings = new List<double[][]>();
            rings.Add(RingToCoords(poly.ExteriorRing));
            foreach (var hole in poly.InteriorRings)
                rings.Add(RingToCoords(hole));
            return rings.ToArray();
        }

        private static double[][] RingToCoords(LineString ring)
        {
            return ring.Coordinates
                .Select(c => new double[] { c.X, c.Y })
                .ToArray();
        }
    }
}