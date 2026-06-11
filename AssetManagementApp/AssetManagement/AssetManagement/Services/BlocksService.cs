using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System.IO;
using Newtonsoft.Json;

namespace AssetManagement.Services
{
    public class BlocksService
    {
        private readonly DataService _db;

        public BlocksService(DataService db)
        {
            _db = db;
        }

        public void ImportBlocksFromGeoJson(string filePath)
        {
            // 1. Read file
            var geoJson = File.ReadAllText(filePath);

            // 2. Parse GeoJSON
            var reader = new GeoJsonReader();
            var featureCollection = reader.Read<FeatureCollection>(geoJson);

            int count = 0;

            // 3. Loop features
            foreach (var feature in featureCollection)
            {
                try
                {
                    var geom = feature.Geometry;

                    // Extract attributes safely
                    var attributes = feature.Attributes;

                    string blockName =
                        attributes.Exists("CatchNames")
                        ? attributes["CatchNames"]?.ToString()
                        : "Unknown";

                    string majCat =
                        attributes.Exists("MAJ_CAT")
                        ? attributes["MAJ_CAT"]?.ToString()
                        : null;

                    double shapeArea =
                        attributes.Exists("Shape_STAr") &&
                        double.TryParse(attributes["Shape_STAr"]?.ToString(), out double a)
                        ? a
                        : 0;

                    double shapeLength =
                        attributes.Exists("Shape_STLe") &&
                        double.TryParse(attributes["Shape_STLe"]?.ToString(), out double l)
                        ? l
                        : 0;

                    // 4. Convert geometry to SQL Server format
                    var wkt = geom.AsText();

                    // 5. Insert into DB
                    string sql = @"
                        INSERT INTO Blocks (BlockName, Geometry, MAJ_CAT, ShapeArea, ShapeLength)
                        VALUES (@blockName, Geometry::STGeomFromText(@wkt, 4326), @majCat, @area, @length)";

                    _db.Execute(sql, new
                    {
                        blockName,
                        wkt,
                        majCat,
                        area = shapeArea,
                        length = shapeLength
                    });

                    count++;
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine("Error importing feature: " + ex.Message);
                }
            }

            Console.WriteLine($"Imported {count} blocks successfully.");
        }
    }
}
