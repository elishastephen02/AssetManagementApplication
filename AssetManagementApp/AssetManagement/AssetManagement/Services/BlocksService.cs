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
                        attributes.Exists("CAT_NAME")
                        ? attributes["CAT_NAME"]?.ToString() ?? "Unknown"
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
                    // 5. Upsert into DB: update the row if a block with this name
                    //    already exists, otherwise insert a new one.
                    string sql = @"
                    -- 1. Update existing block (only fill missing values)
                    UPDATE b
                    SET
                        BlockName =
                            CASE
                                WHEN b.BlockName IS NULL
                                  OR b.BlockName = ''
                                  OR b.BlockName = 'Unknown'
                                THEN @blockName
                                ELSE b.BlockName
                            END,

                        MAJ_CAT =
                            CASE
                                WHEN b.MAJ_CAT IS NULL
                                  OR b.MAJ_CAT = ''
                                THEN @majCat
                                ELSE b.MAJ_CAT
                            END,

                        ShapeArea =
                            CASE
                                WHEN b.ShapeArea IS NULL
                                  OR b.ShapeArea = 0
                                THEN @area
                                ELSE b.ShapeArea
                            END,

                        ShapeLength =
                            CASE
                                WHEN b.ShapeLength IS NULL
                                  OR b.ShapeLength = 0
                                THEN @length
                                ELSE b.ShapeLength
                            END,

                        Geometry =
                            CASE
                                WHEN b.Geometry IS NULL
                                THEN Geometry::STGeomFromText(@wkt,4326)
                                ELSE b.Geometry
                            END

                    FROM Blocks b
                    WHERE
                        ABS(ISNULL(b.ShapeArea,0) - @area) < 0.001
                        AND ABS(ISNULL(b.ShapeLength,0) - @length) < 0.001
                        AND
                        (
                            b.Geometry IS NULL
                            OR b.Geometry.STEquals(Geometry::STGeomFromText(@wkt,4326)) = 1
                        );
                    -- 2. Insert new block if one doesn't already exist
                    IF @@ROWCOUNT = 0
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
                            Geometry::STGeomFromText(@wkt,4326),
                            @majCat,
                            @area,
                            @length
                        )

                    END;
                    ";
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