using Dapper;
using Npgsql;
using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;

namespace Server.Services
{
    public interface IDatabaseInitializerService
    {
        Task InitializeDatabaseAsync();
        bool IsDatabaseInitialized();
    }

    public class DatabaseInitializerService : IDatabaseInitializerService
    {
        private readonly string _connectionString;
        private readonly string _provider;
        private readonly ILogger<DatabaseInitializerService> _logger;

        public DatabaseInitializerService(string connectionString, string provider, ILogger<DatabaseInitializerService> logger)
        {
            _connectionString = connectionString;
            _provider = provider;
            _logger = logger;
        }

        public bool IsDatabaseInitialized()
        {
            try
            {
                using IDbConnection connection = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) 
                    ? new Microsoft.Data.SqlClient.SqlConnection(_connectionString) 
                    : new NpgsqlConnection(_connectionString);
                connection.Open();

                // Check if critical tables exist
                var tablesToCheck = new[] { 
                    "batch", "userscredentials", "objecttypes", 
                    "databaseconnections", "databaselookupmappings", 
                    "ocrproviders", "ocrconnectors", "ocrconfiguration",
                    "dmsproviders", "dmsconnectors", "dmsoutputformats",
                    "propertymapping", "docintelresults", "emailconfigurations", "localfolderconfigurations", "sftpconfigurations"
                };
                
                foreach (var tableName in tablesToCheck)
                {
                    string tableExistsQuery;
                    if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        tableExistsQuery = "SELECT COUNT(*) FROM sys.tables WHERE LOWER(name) = LOWER(@TableName)";
                    }
                    else
                    {
                        tableExistsQuery = @"
                            SELECT EXISTS (
                                SELECT FROM information_schema.tables 
                                WHERE table_schema = 'public' 
                                AND LOWER(table_name) = @TableName
                            );";
                    }

                    var tableExists = connection.QueryFirstOrDefault<bool>(tableExistsQuery, new { TableName = tableName });
                    
                    if (!tableExists)
                    {
                        _logger.LogInformation($"Table '{tableName}' does not exist. Database needs initialization.");
                        return false;
                    }
                }

                _logger.LogInformation("Database schema already exists.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database schema existence");
                return false;
            }
        }

        public async Task InitializeDatabaseAsync()
        {
            _logger.LogInformation("Initializing database/applying migrations...");

            try
            {
                using IDbConnection connection = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? new Microsoft.Data.SqlClient.SqlConnection(_connectionString)
                    : new NpgsqlConnection(_connectionString);
                
                if (connection is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
                else connection.Open();

                string scriptFile = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "SqlServer_Database_Script.sql"
                    : "PostgreSQL_Database_Script.sql";

                // Execute the main schema script
                await ExecuteSchemaScriptAsync(connection, scriptFile);
                _logger.LogInformation($"Main schema script {scriptFile} executed successfully.");

                // Apply incremental migrations (e.g. adding new columns to existing tables)
                await ApplyIncrementalMigrationsAsync(connection);
                _logger.LogInformation("Incremental migrations applied successfully.");

                _logger.LogInformation("Database initialization completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database");
                throw;
            }
        }

        private async Task ApplyIncrementalMigrationsAsync(IDbConnection connection)
        {
            _logger.LogInformation("Checking for incremental migrations...");

            // 1. Add OcrConnectorId to ObjectTypes if it doesn't exist
            string checkColumnSql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                checkColumnSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('ObjectTypes') AND name = 'OcrConnectorId'";
            }
            else
            {
                checkColumnSql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'objecttypes' AND column_name = 'ocrconnectorid'";
            }

            var columnExists = await connection.ExecuteScalarAsync<int>(checkColumnSql) > 0;
            if (!columnExists)
            {
                _logger.LogInformation("Adding OcrConnectorId column to ObjectTypes table...");
                string addColumnSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "ALTER TABLE ObjectTypes ADD OcrConnectorId INT NULL"
                    : "ALTER TABLE objecttypes ADD COLUMN ocrconnectorid INTEGER NULL";
                
                await connection.ExecuteAsync(addColumnSql);
            }

            // 2. Add OcrMode to ObjectTypes if it doesn't exist
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                checkColumnSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('ObjectTypes') AND name = 'OcrMode'";
            }
            else
            {
                checkColumnSql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'objecttypes' AND column_name = 'ocrmode'";
            }

            columnExists = await connection.ExecuteScalarAsync<int>(checkColumnSql) > 0;
            if (!columnExists)
            {
                _logger.LogInformation("Adding OcrMode column to ObjectTypes table...");
                string addColumnSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "ALTER TABLE ObjectTypes ADD OcrMode NVARCHAR(30) DEFAULT 'Manual'"
                    : "ALTER TABLE objecttypes ADD COLUMN ocrmode VARCHAR(30) DEFAULT 'Manual'";
                
                await connection.ExecuteAsync(addColumnSql);
            }

            // Add SeparationMode to ObjectTypes if it doesn't exist
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                checkColumnSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('ObjectTypes') AND name = 'SeparationMode'";
            }
            else
            {
                checkColumnSql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'objecttypes' AND column_name = 'separationmode'";
            }

            columnExists = await connection.ExecuteScalarAsync<int>(checkColumnSql) > 0;
            if (!columnExists)
            {
                _logger.LogInformation("Adding SeparationMode column to ObjectTypes table...");
                string addColumnSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "ALTER TABLE ObjectTypes ADD SeparationMode NVARCHAR(30) DEFAULT 'Global'"
                    : "ALTER TABLE objecttypes ADD COLUMN separationmode VARCHAR(30) DEFAULT 'Global'";
                
                await connection.ExecuteAsync(addColumnSql);
            }

            // 3. Add Ollama Provider if it doesn't exist
            _logger.LogInformation("Checking for Ollama OCR Provider...");
            string checkProviderSql = "SELECT COUNT(*) FROM ocrproviders WHERE LOWER(name) = 'ollama'";
            var providerExists = await connection.ExecuteScalarAsync<int>(checkProviderSql) > 0;
            if (!providerExists)
            {
                _logger.LogInformation("Adding Ollama OCR Provider...");
                string insertProviderSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "INSERT INTO ocrproviders (name, displayname, description, isactive) VALUES ('ollama', 'Ollama (Gemma)', 'Local Ollama LLM extraction', 1);"
                    : "INSERT INTO ocrproviders (name, displayname, description, isactive) VALUES ('ollama', 'Ollama (Gemma)', 'Local Ollama LLM extraction', TRUE);";
                await connection.ExecuteAsync(insertProviderSql);

                string insertConnectorSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive) SELECT id, 'Ollama Local', 0, '{\"endpoint\": \"http://localhost:11434/api/generate\", \"modelid\": \"gemma4:e4b\"}', 1 FROM ocrproviders WHERE LOWER(name) = 'ollama';"
                    : "INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive) SELECT id, 'Ollama Local', FALSE, '{\"endpoint\": \"http://localhost:11434/api/generate\", \"modelid\": \"gemma4:e4b\"}', TRUE FROM ocrproviders WHERE LOWER(name) = 'ollama';";
                await connection.ExecuteAsync(insertConnectorSql);
            }

            // 4. Add DefaultValue and IsEnabled to BatchClassDetail and DocumentClassDetail if they don't exist
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);

            // batchclassdetail - DefaultValue
            string checkBatchDefaultValSql = isSqlServer
                ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('BatchClassDetail') AND name = 'DefaultValue'"
                : "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'batchclassdetail' AND column_name = 'defaultvalue'";
            if (await connection.ExecuteScalarAsync<int>(checkBatchDefaultValSql) == 0)
            {
                _logger.LogInformation("Adding DefaultValue column to BatchClassDetail table...");
                string addSql = isSqlServer
                    ? "ALTER TABLE BatchClassDetail ADD DefaultValue NVARCHAR(1000) NULL"
                    : "ALTER TABLE batchclassdetail ADD COLUMN defaultvalue VARCHAR(1000) NULL";
                await connection.ExecuteAsync(addSql);
            }

            // batchclassdetail - IsEnabled
            string checkBatchIsEnabledSql = isSqlServer
                ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('BatchClassDetail') AND name = 'IsEnabled'"
                : "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'batchclassdetail' AND column_name = 'isenabled'";
            if (await connection.ExecuteScalarAsync<int>(checkBatchIsEnabledSql) == 0)
            {
                _logger.LogInformation("Adding IsEnabled column to BatchClassDetail table...");
                string addSql = isSqlServer
                    ? "ALTER TABLE BatchClassDetail ADD IsEnabled BIT NOT NULL DEFAULT 1"
                    : "ALTER TABLE batchclassdetail ADD COLUMN isenabled BOOLEAN NOT NULL DEFAULT TRUE";
                await connection.ExecuteAsync(addSql);
            }

            // documentclassdetail - DefaultValue
            string checkDocDefaultValSql = isSqlServer
                ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('DocumentClassDetail') AND name = 'DefaultValue'"
                : "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'documentclassdetail' AND column_name = 'defaultvalue'";
            if (await connection.ExecuteScalarAsync<int>(checkDocDefaultValSql) == 0)
            {
                _logger.LogInformation("Adding DefaultValue column to DocumentClassDetail table...");
                string addSql = isSqlServer
                    ? "ALTER TABLE DocumentClassDetail ADD DefaultValue NVARCHAR(1000) NULL"
                    : "ALTER TABLE documentclassdetail ADD COLUMN defaultvalue VARCHAR(1000) NULL";
                await connection.ExecuteAsync(addSql);
            }

            // Recreate views to include new columns
            _logger.LogInformation("Recreating properties views to include DefaultValue and IsEnabled...");
            
            // Recreate BatchTypeProperties View
            string recreateBatchViewSql = isSqlServer
                ? @"CREATE OR ALTER VIEW BatchTypeProperties AS
                    SELECT BatchClassDetail.BatchTypeId, BatchClassDetail.PropertyId, BatchClassDetail.PropertyOrder, 
                           BatchClassDetail.IsPreIndex, BatchClassDetail.IsRequired, BatchClassDetail.ZoneID, 
                           Property.PropertyName, Property.PropertyDesc, Property.PropertyType, BatchClassDetail.Length, 
                           BatchClassDetail.LookupId, BatchClassDetail.Id, BatchClassDetail.DefaultValue, BatchClassDetail.IsEnabled
                    FROM BatchClassDetail 
                    INNER JOIN Property ON BatchClassDetail.PropertyId = Property.Id;"
                : @"CREATE OR REPLACE VIEW BatchTypeProperties AS
                    SELECT BatchClassDetail.BatchTypeId, BatchClassDetail.PropertyId, BatchClassDetail.PropertyOrder, 
                           BatchClassDetail.IsPreIndex, BatchClassDetail.IsRequired, BatchClassDetail.ZoneID, 
                           Property.PropertyName, Property.PropertyDesc, Property.PropertyType, BatchClassDetail.Length, 
                           BatchClassDetail.LookupId, BatchClassDetail.Id, BatchClassDetail.DefaultValue, BatchClassDetail.IsEnabled
                    FROM BatchClassDetail 
                    INNER JOIN Property ON BatchClassDetail.PropertyId = Property.Id;";
            await connection.ExecuteAsync(recreateBatchViewSql);

            // Recreate DocTypeProperties View
            string recreateDocViewSql = isSqlServer
                ? @"CREATE OR ALTER VIEW DocTypeProperties AS
                    SELECT DocumentClassDetail.Id, DocumentClassDetail.DocTypeId, DocumentClassDetail.PropertyId, 
                           DocumentClassDetail.PropertyOrder, DocumentClassDetail.IsEnabled, DocumentClassDetail.IsRequired, 
                           Property.PropertyName, Property.PropertyDesc, Property.PropertyType, DocumentClassDetail.ZoneId, 
                           DocumentClassDetail.Length, DocumentClassDetail.LookupId, DocumentClassDetail.IsBatchProperty, 
                           DocumentClassDetail.DefaultValue
                    FROM DocumentClassDetail 
                    INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id;"
                : @"CREATE OR REPLACE VIEW DocTypeProperties AS
                    SELECT DocumentClassDetail.Id, DocumentClassDetail.DocTypeId, DocumentClassDetail.PropertyId, 
                           DocumentClassDetail.PropertyOrder, DocumentClassDetail.IsEnabled, DocumentClassDetail.IsRequired, 
                           Property.PropertyName, Property.PropertyDesc, Property.PropertyType, DocumentClassDetail.ZoneId, 
                           DocumentClassDetail.Length, DocumentClassDetail.LookupId, DocumentClassDetail.IsBatchProperty, 
                           DocumentClassDetail.DefaultValue
                    FROM DocumentClassDetail 
                    INNER JOIN Property ON DocumentClassDetail.PropertyId = Property.Id;";
            await connection.ExecuteAsync(recreateDocViewSql);

            // 5. Add License and InstallationInfo tables
            string checkLicenseSql = isSqlServer
                ? "SELECT COUNT(*) FROM sys.tables WHERE name = 'license'"
                : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'license'";
            if (await connection.ExecuteScalarAsync<int>(checkLicenseSql) == 0)
            {
                _logger.LogInformation("Creating license table...");
                string createSql = isSqlServer
                    ? @"CREATE TABLE license (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            license_id NVARCHAR(100) NOT NULL,
                            customer_name NVARCHAR(255),
                            license_type NVARCHAR(50) NOT NULL,
                            installation_id NVARCHAR(100) NOT NULL,
                            issued_at_utc DATETIME2 NOT NULL,
                            expires_at_utc DATETIME2,
                            product_version NVARCHAR(50),
                            payload_hash NVARCHAR(255) NOT NULL,
                            last_checked_utc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            activated_at_utc DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            is_active BIT NOT NULL DEFAULT 1
                        )"
                    : @"CREATE TABLE license (
                            id SERIAL PRIMARY KEY,
                            license_id VARCHAR(100) NOT NULL,
                            customer_name VARCHAR(255),
                            license_type VARCHAR(50) NOT NULL,
                            installation_id VARCHAR(100) NOT NULL,
                            issued_at_utc TIMESTAMP NOT NULL,
                            expires_at_utc TIMESTAMP,
                            product_version VARCHAR(50),
                            payload_hash VARCHAR(255) NOT NULL,
                            last_checked_utc TIMESTAMP NOT NULL DEFAULT NOW(),
                            activated_at_utc TIMESTAMP NOT NULL DEFAULT NOW(),
                            is_active BOOLEAN NOT NULL DEFAULT TRUE
                        )";
                await connection.ExecuteAsync(createSql);
            }

            string checkInstallationSql = isSqlServer
                ? "SELECT COUNT(*) FROM sys.tables WHERE name = 'installation_info'"
                : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'installation_info'";
            if (await connection.ExecuteScalarAsync<int>(checkInstallationSql) == 0)
            {
                _logger.LogInformation("Creating installation_info table...");
                string createSql = isSqlServer
                    ? @"CREATE TABLE installation_info (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            installation_id NVARCHAR(100) NOT NULL,
                            created_at_utc DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        )"
                    : @"CREATE TABLE installation_info (
                            id SERIAL PRIMARY KEY,
                            installation_id VARCHAR(100) NOT NULL,
                            created_at_utc TIMESTAMP NOT NULL DEFAULT NOW()
                        )";
                await connection.ExecuteAsync(createSql);

                string insertSql = isSqlServer
                    ? "INSERT INTO installation_info (installation_id, created_at_utc) VALUES (CAST(NEWID() AS NVARCHAR(100)), GETUTCDATE())"
                    : "INSERT INTO installation_info (installation_id, created_at_utc) VALUES (gen_random_uuid()::TEXT, NOW())";
                await connection.ExecuteAsync(insertSql);
            }
        }

        private async Task ExecuteSchemaScriptAsync(IDbConnection connection, string fileName)
        {
            var script = await ReadEmbeddedResourceAsync(fileName);
            if (string.IsNullOrEmpty(script) && _provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                script = await ReadEmbeddedResourceAsync("PostgreSQL_Database_Script.sql");
            }

            if (!string.IsNullOrEmpty(script))
            {
                await ExecuteScriptAsync(connection, script);
            }
            else
            {
                _logger.LogWarning($"Could not find schema script file: {fileName}");
            }
        }
// ... rest of the file ...

        private async Task<string> ReadEmbeddedResourceAsync(string fileName)
        {
            // Look for the file in the Migrations folder first (relative to the executing assembly)
            var executingAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var migrationPath = Path.Combine(executingAssemblyPath, "Migrations", fileName);
            if (File.Exists(migrationPath))
            {
                return await File.ReadAllTextAsync(migrationPath);
            }
        
            // Look for the file in the main directory (relative to the executing assembly)
            var mainPath = Path.Combine(executingAssemblyPath, fileName);
            if (File.Exists(mainPath))
            {
                return await File.ReadAllTextAsync(mainPath);
            }
        
            // Also check for the file in the current working directory
            var currentDirPath = Path.Combine(Directory.GetCurrentDirectory(), "Migrations", fileName);
            if (File.Exists(currentDirPath))
            {
                return await File.ReadAllTextAsync(currentDirPath);
            }
        
            var currentDirMainPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            if (File.Exists(currentDirMainPath))
            {
                return await File.ReadAllTextAsync(currentDirMainPath);
            }
        
            // If not found, try to find any similar file in the migrations directory
            var migrationsDir = Path.Combine(executingAssemblyPath, "Migrations");
            if (Directory.Exists(migrationsDir))
            {
                var files = Directory.GetFiles(migrationsDir, "*.*", SearchOption.TopDirectoryOnly);
                var targetFile = files.FirstOrDefault(f => 
                    Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f).Contains(fileName.Replace(" ", "_").Replace(".txt", "").Replace(".sql", ""), StringComparison.OrdinalIgnoreCase));
                if (targetFile != null)
                {
                    return await File.ReadAllTextAsync(targetFile);
                }
            }
        
            // As a fallback, check for files in the project's root directory
            var projectRoot = Path.GetFullPath(Path.Combine(executingAssemblyPath, "..", "..", "..", "..", "..", "..")); // Navigate to project root
            var projectMigrationPath = Path.Combine(projectRoot, "Migrations", fileName);
            if (File.Exists(projectMigrationPath))
            {
                return await File.ReadAllTextAsync(projectMigrationPath);
            }
        
            var projectRootPath = Path.Combine(projectRoot, fileName);
            if (File.Exists(projectRootPath))
            {
                return await File.ReadAllTextAsync(projectRootPath);
            }
        
            _logger.LogWarning($"Migration script file '{fileName}' not found in expected locations.");
            return string.Empty;
        }

        private async Task ExecuteScriptAsync(IDbConnection connection, string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return;

            // Split the script into individual statements
            var statements = SplitSqlScript(script);

            foreach (var statement in statements)
            {
                var trimmedStatement = statement.Trim();
                if (string.IsNullOrEmpty(trimmedStatement))
                    continue;

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = trimmedStatement;
                    if (cmd is System.Data.Common.DbCommand dbCmd) await dbCmd.ExecuteNonQueryAsync();
                    else cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    // Log the error but continue with other statements
                    _logger.LogWarning(ex, "Error executing SQL statement: {Statement}", trimmedStatement);
                }
            }
        }

        private List<string> SplitSqlScript(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return new List<string>();

            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                // Split by GO on its own line (standard SQL Server batch separator)
                return Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .Select(b => b.Trim())
                    .ToList();
            }

            // PostgreSQL logic (current implementation)
            var statements = new List<string>();
            var currentStatement = "";
            var inMultiLineComment = false;
            var inSingleLineComment = false;
            var inStringLiteral = false;
            var stringDelimiter = '\0';
            var inDollarQuote = false;
            var dollarQuoteTag = "";
            var potentialDollarStart = false;

            for (int i = 0; i < script.Length; i++)
            {
                char c = script[i];
                char nextChar = (i + 1 < script.Length) ? script[i + 1] : '\0';

                // Check for dollar quote start
                if (c == '$' && !inStringLiteral && !inMultiLineComment && !inSingleLineComment)
                {
                    // Look for potential tag after $
                    var tagStart = i + 1;
                    var tagEnd = tagStart;
                    while (tagEnd < script.Length && (char.IsLetterOrDigit(script[tagEnd]) || script[tagEnd] == '_'))
                    {
                        tagEnd++;
                    }
                    if (tagEnd < script.Length && script[tagEnd] == '$')
                    {
                        // Found a dollar quote start
                        dollarQuoteTag = script.Substring(tagStart, tagEnd - tagStart);
                        var tagLength = 1 + dollarQuoteTag.Length + 1; // $ + tag + $
                        
                        if (!inDollarQuote)
                        {
                            inDollarQuote = true;
                        }
                        else if (dollarQuoteTag == "")
                        {
                            // Closing the same type of dollar quote
                            inDollarQuote = false;
                            dollarQuoteTag = "";
                        }
                        
                        // Add the entire tag to current statement
                        currentStatement += script.Substring(i, tagLength);
                        i += tagLength - 1; // Adjust for loop increment
                        continue;
                    }
                }
                
                // Check for dollar quote end (same tag)
                if (inDollarQuote && c == '$')
                {
                    var tagStart = i + 1;
                    if (tagStart <= script.Length && dollarQuoteTag.Length > 0)
                    {
                        var remainingLength = script.Length - tagStart;
                        if (remainingLength >= dollarQuoteTag.Length && 
                            script.Substring(tagStart, dollarQuoteTag.Length) == dollarQuoteTag &&
                            tagStart + dollarQuoteTag.Length < script.Length &&
                            script[tagStart + dollarQuoteTag.Length] == '$')
                        {
                            // Found matching end tag
                            var tagLength = 1 + dollarQuoteTag.Length + 1; // $ + tag + $
                            currentStatement += script.Substring(i, tagLength);
                            i += tagLength - 1; // Adjust for loop increment
                            inDollarQuote = false;
                            dollarQuoteTag = "";
                            continue;
                        }
                    }
                }

                // Handle multi-line comment start/end
                if (!inStringLiteral && !inDollarQuote && !inSingleLineComment && c == '/' && nextChar == '*')
                {
                    inMultiLineComment = true;
                    i++; // Skip the next character
                    continue;
                }
                if (inMultiLineComment && c == '*' && nextChar == '/')
                {
                    inMultiLineComment = false;
                    i++; // Skip the next character
                    continue;
                }

                // Handle single-line comment start
                if (!inStringLiteral && !inDollarQuote && !inMultiLineComment && c == '-' && nextChar == '-')
                {
                    inSingleLineComment = true;
                    i++; // Skip the next character
                    continue;
                }

                // Handle end of single-line comment
                if (inSingleLineComment && (c == '\n' || c == '\r'))
                {
                    inSingleLineComment = false;
                    continue;
                }

                // Skip if inside comments
                if (inMultiLineComment || inSingleLineComment)
                {
                    continue;
                }

                // Handle string literals
                if ((c == '\'' || c == '"') && !inStringLiteral && !inDollarQuote)
                {
                    inStringLiteral = true;
                    stringDelimiter = c;
                    currentStatement += c;
                    continue;
                }
                else if (c == stringDelimiter && inStringLiteral)
                {
                    // Check if it's an escaped quote (double quote)
                    if (i + 1 < script.Length && script[i + 1] == stringDelimiter)
                    {
                        currentStatement += c;
                        currentStatement += script[++i]; // Add the escaped quote and advance index
                        continue;
                    }
                    else
                    {
                        inStringLiteral = false;
                        stringDelimiter = '\0';
                        currentStatement += c;
                        continue;
                    }
                }

                // Process semicolon only if not in string literal or dollar quote
                if (c == ';' && !inStringLiteral && !inDollarQuote)
                {
                    currentStatement += c;
                    statements.Add(currentStatement.Trim());
                    currentStatement = "";
                    continue;
                }

                currentStatement += c;
            }

            // Add the last statement if it doesn't end with semicolon
            if (!string.IsNullOrWhiteSpace(currentStatement))
            {
                statements.Add(currentStatement.Trim());
            }

            return statements;
        }
    }
}