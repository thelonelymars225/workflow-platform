using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Workflow.Api.Data.Seeding;

// Multi-row INSERT ... ON CONFLICT DO NOTHING built from the EF model, so repeated or concurrent seeds never overwrite rows.
public static class InsertIgnore
{
    private const int MaxParameters = 30_000;

    public static async Task<int> RunAsync<T>(WorkflowDbContext db, IReadOnlyList<T> rows, CancellationToken cancellationToken = default)
        where T : class
    {
        if (rows.Count == 0) return 0;
        var entityType = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var properties = entityType.GetProperties().ToList();
        var columns = string.Join(", ", properties.Select(p => $"\"{p.GetColumnName(table)}\""));
        var batchSize = Math.Max(1, MaxParameters / properties.Count);
        var inserted = 0;

        foreach (var batch in rows.Chunk(batchSize))
        {
            var parameters = new List<NpgsqlParameter>(batch.Length * properties.Count);
            var values = new List<string>(batch.Length);
            foreach (var row in batch)
            {
                var placeholders = new List<string>(properties.Count);
                foreach (var property in properties)
                {
                    var value = property.PropertyInfo!.GetValue(row);
                    var converter = property.GetTypeMapping().Converter;
                    if (value is not null && converter is not null) value = converter.ConvertToProvider(value);
                    var name = "p" + parameters.Count;
                    parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
                    placeholders.Add("@" + name);
                }
                values.Add("(" + string.Join(", ", placeholders) + ")");
            }
            var sql = $"INSERT INTO \"{table.Name}\" ({columns}) VALUES {string.Join(", ", values)} ON CONFLICT DO NOTHING";
            inserted += await db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
        return inserted;
    }
}
