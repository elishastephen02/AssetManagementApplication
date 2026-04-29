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

        public ImportResult ImportGeoJson(string geoJson)
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
                    InsertFeature(feature, writer);
                    inserted++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    _logger.LogError(ex, "GeoJSON import failed");
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
                        SEGID NVARCHAR(100) NOT NULL UNIQUE,
                        GEOMETRY_DATA geometry NOT NULL,
                        DATE_IMPORTED DATETIME DEFAULT GETDATE()
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

        private void InsertFeature(IFeature feature, WKTWriter writer)
        {
            var segId = feature.Attributes["SEGID"]?.ToString();
            var wkt = writer.Write(feature.Geometry);

            var columns = new List<string> { "SEGID", "GEOMETRY_DATA" };
            var values = new List<string>
            {
                "@SegId",
                "geometry::STGeomFromText(@Wkt, 4326)"
            };

            var parameters = new Dictionary<string, object>
            {
                ["SegId"] = segId,
                ["Wkt"] = wkt
            };

            var reservedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SEGID", "GEOMETRY_DATA", "GEO_PK", "DATE_IMPORTED"
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
                INSERT INTO GEOJSON ({string.Join(",", columns)})
                VALUES ({string.Join(",", values)})
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