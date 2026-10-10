using Npgsql;

var connString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection for the read-only DB audit.");
await using var conn = new NpgsqlConnection(connString);
await conn.OpenAsync();

async Task<object?> ScalarAsync(string sql) {
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.CommandTimeout = 30;
    return await cmd.ExecuteScalarAsync();
}

async Task<List<string[]>> QueryAsync(string sql) {
    var rows = new List<string[]>();
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.CommandTimeout = 30;
    await using var rdr = await cmd.ExecuteReaderAsync();
    var cols = rdr.FieldCount;
    while (await rdr.ReadAsync()) {
        var row = new string[cols];
        for (int i = 0; i < cols; i++) row[i] = rdr.IsDBNull(i) ? "NULL" : rdr.GetValue(i)!.ToString()!;
        rows.Add(row);
    }
    return rows;
}

void Print(string label, List<string[]> rows) {
    Console.WriteLine("=== " + label + " (" + rows.Count + " rows) ===");
    foreach (var r in rows) Console.WriteLine(string.Join(" | ", r));
}

Console.WriteLine("=== CASCADE ANALYSIS ===\n");

// player_hero referencing 40000000 heroes
var ph400 = await QueryAsync(@"SELECT ph.player_id::text, ph.hero_id::text, h.code, u.display_name 
FROM hero_chess.player_hero ph 
JOIN hero_chess.hero h ON h.id=ph.hero_id 
JOIN hero_chess.user_account u ON u.id=ph.player_id 
WHERE ph.hero_id >= '40000000-0000-4000-8000-000000000001'::uuid 
  AND ph.hero_id < '60000000-0000-4000-8000-000000000001'::uuid 
ORDER BY ph.player_id");
Print("player_hero referencing 40000000 heroes:", ph400);

// player_hero referencing 60000000 dev-slot heroes (0001-0020)
var phDevSlot = await QueryAsync(@"SELECT ph.player_id::text, ph.hero_id::text, h.code, u.display_name 
FROM hero_chess.player_hero ph 
JOIN hero_chess.hero h ON h.id=ph.hero_id 
JOIN hero_chess.user_account u ON u.id=ph.player_id 
WHERE ph.hero_id >= '60000000-0000-4000-8000-000000000001'::uuid 
  AND ph.hero_id < '60000000-0000-4000-8000-000000000021'::uuid 
ORDER BY ph.player_id");
Print("player_hero referencing 60000000 dev-slot heroes (001-020):", phDevSlot);

// player_hero for real users only (not dev accounts)
var phReal = await QueryAsync(@"SELECT ph.player_id::text, ph.hero_id::text, h.code, u.display_name 
FROM hero_chess.player_hero ph 
JOIN hero_chess.hero h ON h.id=ph.hero_id 
JOIN hero_chess.user_account u ON u.id=ph.player_id 
WHERE u.id != '50000000-0000-4000-8000-000000000001'::uuid 
  AND u.id != '50000000-0000-4000-8000-000000000002'::uuid 
ORDER BY ph.player_id");
Print("player_hero owned by real users:", phReal);

// game_match count
Console.WriteLine("\ngame_match: " + await ScalarAsync("SELECT COUNT(*) FROM hero_chess.game_match") + " (0 = safe)");
Console.WriteLine("match_participant: " + await ScalarAsync("SELECT COUNT(*) FROM hero_chess.match_participant"));
Console.WriteLine("admin_audit_log: " + await ScalarAsync("SELECT COUNT(*) FROM hero_chess.admin_audit_log"));

// lineup
var lineup = await QueryAsync(@"SELECT l.id::text, l.player_id::text, u.display_name, l.name 
FROM hero_chess.lineup l 
JOIN hero_chess.user_account u ON u.id=l.player_id 
ORDER BY l.player_id");
Print("LINEUP:", lineup);

// auth_identity
Print("AUTH_IDENTITY:", await QueryAsync("SELECT id::text, user_id::text, provider FROM hero_chess.auth_identity ORDER BY user_id"));

// Cascade simulation: rows that would be cascade-deleted
var cascadeHeroIds = await QueryAsync(@"SELECT id::text, code FROM hero_chess.hero 
WHERE id < '60000000-0000-4000-8000-000000000001'::uuid 
   OR (id >= '60000000-0000-4000-8000-000000000001'::uuid AND id < '60000000-0000-4000-8000-000000000021'::uuid) 
   OR code LIKE 'dev-shop-%' 
ORDER BY id");
Print("Heroes to be cascade-deleted:", cascadeHeroIds);

var phCascade = await ScalarAsync(@"SELECT COUNT(*) FROM hero_chess.player_hero ph 
WHERE ph.hero_id IN (SELECT id FROM hero_chess.hero WHERE id < '60000000-0000-4000-8000-000000000001'::uuid 
   OR (id >= '60000000-0000-4000-8000-000000000001'::uuid AND id < '60000000-0000-4000-8000-000000000021'::uuid) 
   OR code LIKE 'dev-shop-%')");
Console.WriteLine("\nplayer_hero rows to cascade-delete: " + phCascade);

var leCascade = await ScalarAsync(@"SELECT COUNT(*) FROM hero_chess.lineup_entry le 
WHERE le.hero_id IN (SELECT id FROM hero_chess.hero WHERE id < '60000000-0000-4000-8000-000000000001'::uuid 
   OR (id >= '60000000-0000-4000-8000-000000000001'::uuid AND id < '60000000-0000-4000-8000-000000000021'::uuid) 
   OR code LIKE 'dev-shop-%')");
Console.WriteLine("lineup_entry rows to cascade-delete: " + leCascade);

// Remaining after cascade
var phRemaining = await ScalarAsync(@"SELECT COUNT(*) FROM hero_chess.player_hero ph 
WHERE ph.hero_id NOT IN (SELECT id FROM hero_chess.hero WHERE id < '60000000-0000-4000-8000-000000000001'::uuid 
   OR (id >= '60000000-0000-4000-8000-000000000001'::uuid AND id < '60000000-0000-4000-8000-000000000021'::uuid) 
   OR code LIKE 'dev-shop-%')");
Console.WriteLine("player_hero rows REMAINING after cascade: " + phRemaining);

// Code conflict analysis: which 40000000 heroes have code matching catalog
var conflict = await QueryAsync(@"SELECT h.id::text, h.code, h.name, 
CASE WHEN EXISTS (SELECT 1 FROM hero_chess.hero WHERE code = h.code AND id >= '60000000-0000-4000-8000-000000000001'::uuid AND id < '60000000-0000-4000-8000-000000000046'::uuid) THEN 'CONFLICT_WITH_CATALOG' ELSE 'NO_CONFLICT' END AS conflict_status 
FROM hero_chess.hero h 
WHERE h.id < '50000000-0000-4000-8000-000000000001'::uuid 
ORDER BY h.id");
Print("40000000 heroes with catalog code conflict check:", conflict);

Console.WriteLine("\n=== CASCADE ANALYSIS COMPLETE ===");
