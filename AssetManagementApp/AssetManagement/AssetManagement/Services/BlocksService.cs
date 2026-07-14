using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using System;
using System.IO;
using System.Linq;
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
            int errorCount = 0;
            int featureIndex = 0;

            // 3. Loop features
            foreach (var feature in featureCollection)
            {
                featureIndex++;

                try
                {
                    var geom = feature.Geometry;
                    var attributes = feature.Attributes;

                    //Console.WriteLine($"[{featureIndex}] Attrs: {string.Join(", ", attributes.GetNames())}");

                    // --- BlockName ---
                    string blockName = "Unknown";
                    if (attributes.Exists("BlockName"))
                    {
                        var rawName = attributes["BlockName"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(rawName))
                        {
                            blockName = rawName.Trim();
                        }
                        else
                        {
                            Console.WriteLine($"[Feature {featureIndex}] BlockName attribute exists but is null/empty.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Feature {featureIndex}] BlockName attribute NOT FOUND. Available keys: {string.Join(", ", attributes.GetNames())}");
                    }

                    // --- MAJ_CAT (legitimately null for some features, e.g. Phase3/CBD blocks) ---
                    string majCat =
                        attributes.Exists("MAJ_CAT")
                        ? attributes["MAJ_CAT"]?.ToString()
                        : null;

                    double shapeArea = 0;
                    if (attributes.Exists("Shape_Area"))
                    {
                        var rawArea = attributes["Shape_Area"];
                        if (rawArea != null && double.TryParse(rawArea.ToString(), out double a))
                        {
                            shapeArea = a;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Feature {featureIndex}] '{blockName}': Shape_Area attribute not found.");
                    }

                    double shapeLength = geom?.Length ?? 0;

                    // 4. Convert geometry to SQL Server format
                    var wkt = geom.AsText();

                    // 5. Insert into DB
                    string sql = @"
                    IF EXISTS (SELECT 1 FROM Blocks WHERE BlockName = @blockName)
                    BEGIN
                        UPDATE Blocks
                        SET
                            Geometry = Geometry::STGeomFromText(@wkt, 4326),
                            MAJ_CAT = @majCat,
                            ShapeArea = @area,
                            ShapeLength = @length
                        WHERE BlockName = @blockName;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO Blocks
                        (
                            BlockName,
                            Geometry,
                            MAJ_CAT,
                            ShapeArea,
                            ShapeLength
                        )
                        VALUES
                        (
                            @blockName,
                            Geometry::STGeomFromText(@wkt, 4326),
                            @majCat,
                            @area,
                            @length
                        );
                    END";

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
                catch (Exception ex)
                {
                    errorCount++;
                    Console.WriteLine($"[Feature {featureIndex}] Error importing feature: {ex.Message}");
                }
            }

            Console.WriteLine($"Imported {count} blocks successfully. {errorCount} feature(s) failed.");
        }
    }
}