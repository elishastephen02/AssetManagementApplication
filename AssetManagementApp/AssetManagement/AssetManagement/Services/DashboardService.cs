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
                    agg.ConditionR,
                    b.Geometry.STAsText() AS GeomWkt
                FROM Blocks b
                JOIN (
                    SELECT 
                        g.BlockName,
                        AVG(TRY_CAST(g.ConditionR AS DECIMAL(10,2))) AS ConditionR
                    FROM GEOJSON g
                    GROUP BY g.BlockName
                ) agg ON agg.BlockName = b.BlockName
                WHERE b.Geometry IS NOT NULL
            ";

            var rows = _db.Query(sql);
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var wktReader = new WKTReader(factory);
            var features = new List<object>();

            foreach (var row in rows)
            {
                if (row.GeomWkt == null) continue;

                try
                {
                    string wkt = (string)row.GeomWkt;
                    if (wkt.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
                        wkt = wkt.Substring(wkt.IndexOf(';') + 1);

                    Geometry geom = wktReader.Read(wkt);
                    double condition = row.ConditionR != null ? (double)row.ConditionR : 0;

                    var geometryJson = GeometryToGeoJsonObject(geom);
                    if (geometryJson == null)
                    {
                        Console.WriteLine($"Skipping {row.BlockName}: unsupported geometry type {geom.GeometryType}");
                        continue;
                    }

                    features.Add(new
                    {
                        type = "Feature",
                        properties = new { blockName = (string)row.BlockName, condition },
                        geometry = geometryJson
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error on {row.BlockName}: {ex.Message}");
                }
            }

            Console.WriteLine($"Returning {features.Count} features");
            return new { type = "FeatureCollection", features };
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