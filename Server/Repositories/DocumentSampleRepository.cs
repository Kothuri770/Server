using Dapper;
using Npgsql;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface IDocumentSampleRepository
    {
        Task<DocumentSampleDto?> GetByDocTypeIdAsync(int docTypeId);
        Task<int> InsertAsync(DocumentSampleDto documentSample);
        Task<bool> UpdateAsync(DocumentSampleDto documentSample);
        Task<bool> DeleteAsync(int docTypeId);
    }

    public class DocumentSampleRepository : BaseRepository, IDocumentSampleRepository
    {
        public DocumentSampleRepository(string connectionString, string provider) : base(connectionString, provider) { }

        // Use the base CreateConnection method to avoid hiding inherited member

        public async Task<DocumentSampleDto?> GetByDocTypeIdAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = "SELECT DocTypeID, SampleFile \n                        FROM DocumentSample \n                        WHERE DocTypeID = @docTypeId";
            return await conn.QueryFirstOrDefaultAsync<DocumentSampleDto>(sql, new { docTypeId });
        }

        public async Task<int> InsertAsync(DocumentSampleDto documentSample)
        {
            using var conn = CreateConnection();
            var sql = @"INSERT INTO DocumentSample (DocTypeID, SampleFile) 
                        VALUES (@DocTypeID, @SampleFile)";
            var result = await conn.ExecuteAsync(sql, documentSample);
            return result > 0 ? documentSample.DocTypeID : 0;
        }

        public async Task<bool> UpdateAsync(DocumentSampleDto documentSample)
        {
            using var conn = CreateConnection();
            var sql = @"UPDATE DocumentSample 
                        SET SampleFile = @SampleFile 
                        WHERE DocTypeID = @DocTypeID";
            var result = await conn.ExecuteAsync(sql, documentSample);
            return result > 0;
        }

        public async Task<bool> DeleteAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = "DELETE FROM DocumentSample WHERE DocTypeID = @docTypeId";
            var result = await conn.ExecuteAsync(sql, new { docTypeId });
            return result > 0;
        }
    }
}