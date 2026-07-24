using NetTopologySuite.Features;
using NetTopologySuite.IO;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AssetManagement.Services
{
    public class ImportService
    {
        private readonly DataService _db;
        private readonly ILogger<ImportService> _logger;

        public ImportService(DataService db, ILogger<ImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public ImportResult ImportGeoJson(string geoJson, string blockName)
        {
            EnsureGeoJsonTableExists();

            var reader = new GeoJsonReader();
            var features = reader.Read<FeatureCollection>(geoJson);
            var writer = new WKTWriter();

            var properties = ExtractProperties(features);
            EnsureColumnsExist(properties);

            int inserted = 0;
            int skipped = 0;

            foreach (var feature in features)
            {
                try
                {
                    InsertFeature(feature, writer, blockName);
                    inserted++;
                }
                catch (Exception ex)
                {
                    skipped++;

                    Console.WriteLine(ex.ToString());

                    _logger.LogError(ex, ex.ToString());
                }
            }

            return new ImportResult
            {
                Success = true,
                Inserted = inserted,
                Skipped = skipped
            };
        }

        private void EnsureGeoJsonTableExists()
        {
            _db.Execute(@"
                IF OBJECT_ID('GEOJSON', 'U') IS NULL
                BEGIN
                    CREATE TABLE GEOJSON
                    (
                        GEO_PK INT IDENTITY(1,1) PRIMARY KEY,
                        SEGID NVARCHAR(100) NOT NULL,
                        BlockName NVARCHAR(255) NULL,
                        GEOMETRY_DATA geometry NOT NULL
                    )
                END
            ");
        }

        private List<string> ExtractProperties(FeatureCollection features)
        {
            var props = new List<string>();

            foreach (var feature in features)
            {
                foreach (var name in feature.Attributes.GetNames())
                {
                    var clean = CleanColumn(name);

                    if (!props.Contains(clean))
                        props.Add(clean);
                }
            }

            return props;
        }

        private string CleanColumn(string name)
        {
            return Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        }

        private void EnsureColumnsExist(List<string> properties)
        {
            foreach (var prop in properties)
            {
                var exists = _db.QuerySingle<int>(@"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'GEOJSON'
                    AND COLUMN_NAME = @Col",
                new { Col = prop });

                if (exists == 0)
                {
                    _db.Execute($@"
                        ALTER TABLE GEOJSON
                        ADD [{prop}] NVARCHAR(255) NULL
                    ");
                }
            }
        }

        private void InsertFeature(IFeature feature, WKTWriter writer, string blockName)
        {
            var segId = feature.Attributes.Exists("SEGID")
            ? feature.Attributes["SEGID"]?.ToString()
            : null;

            if (string.IsNullOrWhiteSpace(segId))
            {
                _logger.LogWarning("Skipping feature because SEGID is missing.");
                return;
            }

            var exists = _db.QuerySingle<int>(@"
                SELECT COUNT(*)
                FROM GEOJSON
                WHERE SEGID = @SegId",
            new { SegId = segId });

            string? x = null;
            string? y = null;
            string? desDate = null;

            // DESDATE from attributes
            foreach (var attribute in feature.Attributes.GetNames())
            {
                if (attribute.Equals("DESDATE", StringComparison.OrdinalIgnoreCase))
                {
                    desDate = feature.Attributes[attribute]?.ToString();
                }
            }

            // X and Y from the geometry
            if (feature.Geometry != null)
            {
                var coordinate = feature.Geometry.Coordinate;

                if (coordinate != null)
                {
                    x = coordinate.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    y = coordinate.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            // exists -> ignore, else insert all
            if (exists > 0)
            {
                _logger.LogInformation($"SEGID {segId} already exists. Skipping.");
                return;
            }

            // Existing record - update only missing data
            //if (exists > 0)
            //{
            //    _db.Execute(@"
            //    UPDATE GEOJSON
            //    SET
            //        X = CASE
            //                WHEN X IS NULL OR LTRIM(RTRIM(X)) = ''
            //                THEN @X
            //                ELSE X
            //            END,

            //        Y = CASE
            //                WHEN Y IS NULL OR LTRIM(RTRIM(Y)) = ''
            //                THEN @Y
            //                ELSE Y
            //            END,

            //        DESDATE = CASE
            //                WHEN DESDATE IS NULL OR LTRIM(RTRIM(DESDATE)) = ''
            //                THEN @DESDATE
            //                ELSE DESDATE
            //            END
            //    WHERE SEGID = @SegId",
            //    new
            //    {
            //        SegId = segId,
            //        X = x,
            //        Y = y,
            //        DESDATE = desDate
            //    });

            //    return;
            //}

            // block name change
            //if (exists > 0)
            //{
            //    _db.Execute(@"
            //        UPDATE GEOJSON
            //        SET BlockName = @BlockName
            //        WHERE SEGID = @SegId",
            //        new
            //        {
            //            SegId = segId,
            //            BlockName = blockName
            //        });

            //    return;
            //}

            // New record - insert everything
            var wkt = writer.Write(feature.Geometry);

            var columns = new List<string>
            {
                "SEGID",
                "BlockName",
                "GEOMETRY_DATA"
            };

            var values = new List<string>
            {
                "@SegId",
                "@BlockName",
                "geometry::STGeomFromText(@Wkt,4326)"
            };

            var parameters = new Dictionary<string, object>
            {
                ["SegId"] = segId,
                ["BlockName"] = blockName,
                ["Wkt"] = wkt
            };

            var reservedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SEGID",
                "BlockName",
                "GEOMETRY_DATA",
                "GEO_PK"
            };

            foreach (var name in feature.Attributes.GetNames())
            {
                var clean = CleanColumn(name);

                if (reservedColumns.Contains(clean))
                    continue;

                columns.Add($"[{clean}]");
                values.Add($"@{clean}");
                parameters[clean] = feature.Attributes[name]?.ToString();
            }

            var sql = $@"
                INSERT INTO GEOJSON
                ({string.Join(",", columns)})
                VALUES
                ({string.Join(",", values)})
            ";

            _db.Execute(sql, parameters);
        }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public int Inserted { get; set; }
        public int Skipped { get; set; }
    }
}