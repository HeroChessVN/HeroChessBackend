// Minimal read-only DB auditor. DO NOT MODIFY DATABASE.
using Npgsql;

var connString = "Host=aws-0-ap-northeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.rvcvlobhtoqiojwqywoy;Password=FA26SE307ZZ;SSL Mode=Require";

await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();

async Task<object?> ScalarAsync(string sql)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.CommandTimeout = 30;
    return await cmd.ExecuteScalarAsync();
}

async Task<List<string[]>> QueryAsync(string sql)
{
    var rows = new List<string[]>();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.CommandTimeout = 30;
    await using var rdr = await cmd.ExecuteReaderAsync();
    var cols = rdr.FieldCount;
    while (await rdr.ReadAsync())
    {
        var row = new string[cols];
        for (int i = 0; i < cols; i++) row[i] = rdr.IsDBNull(i) ? "NULL" : rdr.GetValue(i)!.ToString()!;
        rows.Add(row);
    }
    return rows;
}

void Print(string label, List<string[]> rows)
{
    Console.WriteLine($"=== {label} ({rows.Count} rows) ===");
    foreach (var row in rows.Take(50)) Console.WriteLine(string.Join(" | ", row));
    if (rows.Count > 50) Console.WriteLine($"... {rows.Count - 50} more rows");
}

Console.WriteLine("=== HERO CHESS DB AUDIT (READ-ONLY) ===");

// A. Hero count
var count = await ScalarAsync("SELECT COUNT(*) FROM hero_chess.hero");
Console.WriteLine($"A. hero count: {count}");

// B. All hero IDs, codes, names
var heroes = await QueryAsync("SELECT id::text, code, name, class_code, character_id::text, trait_id::text, is_enabled, is_test_fixture, setup_points, coin_price FROM hero_chess.hero ORDER BY id");
Print("B. All heroes", heroes);

// C. Check if the 45 catalog UUIDs exist
var catalogIds = Enumerable.Range(1, 45).Select(i => $"'60000000-0000-4000-8000-{i:D12}'").ToList();
var idChunks = catalogIds.Chunk(10);
int found = 0;
foreach (var chunk in idChunks)
{
    var r = await ScalarAsync($"SELECT COUNT(*) FROM hero_chess.hero WHERE id IN ({string.Join(",", chunk)})") ?? "0";
    found += int.Parse(r.ToString()!);
}
Console.WriteLine($"C. Catalog UUIDs found in DB: {found}/45");

// D. Hero IDs outside expected ranges
var unexpected = await QueryAsync(@"
SELECT id::text, code, name FROM hero_chess.hero
WHERE (id < '60000000-0000-4000-8000-000000000001'::uuid AND id >= '40000000-0000-4000-8000-000000000001'::uuid)
   OR (id > '60000000-0000-4000-8000-000000000045'::uuid AND id < '70000000-0000-4000-8000-000000000001'::uuid)
   OR (id >= '70000000-0000-4000-8000-000000000001'::uuid)
ORDER BY id");
Print("D. Heroes outside catalog range (400/600/700 ranges)", unexpected);

// E. Duplicate codes
var dupes = await QueryAsync("SELECT code, COUNT(*) as cnt FROM hero_chess.hero GROUP BY code HAVING COUNT(*) > 1 ORDER BY code");
Print("E. Duplicate hero codes", dupes);

// F. Historical character count
var hcCount = await ScalarAsync("SELECT COUNT(*) FROM hero_chess.historical_character");
Console.WriteLine($"F. historical_character count: {hcCount}");

// G. Chess class count and codes
var classes = await QueryAsync("SELECT code, name_vi, base_sp FROM hero_chess.chess_class ORDER BY code");
Print("G. chess_class", classes);

// H. Hero trait count and codes
var traits = await QueryAsync("SELECT id::text, code, name, kind, implementation_key FROM hero_chess.hero_trait ORDER BY id");
Print("H. hero_trait", traits);

// I. hero table constraints
var constraints = await QueryAsync(@"
SELECT conname, pg_get_constraintdef(oid) AS def
FROM pg_constraint
WHERE conrelid = 'hero_chess.hero'::regclass AND contype IN ('p','u')
ORDER BY contype, conname");
Print("I. hero constraints (p=primary key, u=unique)", constraints);

// J. Triggers on hero
var triggers = await QueryAsync(@"
SELECT tgname, tgtype, pg_get_triggerdef(oid)::text as def
FROM pg_trigger
WHERE tgrelid = 'hero_chess.hero'::regclass AND NOT tgisinternal
ORDER BY tgname");
Print("J. hero triggers (non-internal)", triggers);

// K. Schema table count
var tableCount = await ScalarAsync("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='hero_chess' AND table_type='BASE TABLE'");
Console.WriteLine($"K. hero_chess base tables: {tableCount}");

// L. Check user_account exists (post-migration)
var uaCount = await ScalarAsync("SELECT COUNT(*) FROM hero_chess.user_account");
Console.WriteLine($"L. user_account count: {uaCount}");

// M. ruleset count
var ruleset = await QueryAsync("SELECT id::text, code, name, setup_budget, is_active FROM hero_chess.ruleset ORDER BY code");
Print("M. ruleset", ruleset);

Console.WriteLine("=== AUDIT COMPLETE ===");
