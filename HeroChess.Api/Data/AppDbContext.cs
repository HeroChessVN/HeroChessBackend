// Vai trò file: EF Core unit of work, gần với EntityManager: DbSet là cửa truy vấn bảng; OnModelCreating map model vào schema SQL có sẵn.
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AuthIdentity> AuthIdentities => Set<AuthIdentity>();
    public DbSet<PlayerWallet> Wallets => Set<PlayerWallet>();
    public DbSet<PlayerRating> Ratings => Set<PlayerRating>();
    public DbSet<Ruleset> Rulesets => Set<Ruleset>();
    public DbSet<ChessClass> ChessClasses => Set<ChessClass>();
    public DbSet<LineupSlot> LineupSlots => Set<LineupSlot>();
    public DbSet<HistoricalCharacter> HistoricalCharacters => Set<HistoricalCharacter>();
    public DbSet<HeroTrait> HeroTraits => Set<HeroTrait>();
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<Faction> Factions => Set<Faction>();
    public DbSet<HeroFaction> HeroFactions => Set<HeroFaction>();
    public DbSet<TeamSkill> TeamSkills => Set<TeamSkill>();
    public DbSet<Cosmetic> Cosmetics => Set<Cosmetic>();
    public DbSet<PlayerHero> PlayerHeroes => Set<PlayerHero>();
    public DbSet<PlayerCosmetic> PlayerCosmetics => Set<PlayerCosmetic>();
    public DbSet<SavedLineup> Lineups => Set<SavedLineup>();
    public DbSet<SavedLineupEntry> LineupEntries => Set<SavedLineupEntry>();
    public DbSet<SavedLineupSkill> LineupSkills => Set<SavedLineupSkill>();
    public DbSet<GameMatch> Matches => Set<GameMatch>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<MatchState> MatchStates => Set<MatchState>();
    public DbSet<MatchAction> MatchActions => Set<MatchAction>();
    public DbSet<CoinTransaction> CoinTransactions => Set<CoinTransaction>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

    // OnModelCreating: Khai báo bảng, khóa, quan hệ và cột jsonb; đây là mapping, không phải lệnh tạo/migrate DB.
    protected override void OnModelCreating(ModelBuilder model)
    {
        // TPH: User/Player/Admin cùng bảng; truy vấn DbSet<Player> tự lọc role=player.
        model.Entity<User>(e =>
        {
            Table(e, "user_account");
            e.HasDiscriminator(x => x.Role).HasValue<User>("unassigned").HasValue<Player>("player").HasValue<Admin>("admin");
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
        });
        model.Entity<AuthIdentity>(e => { Table(e, "auth_identity"); e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId); });
        model.Entity<PlayerWallet>(e => { e.ToTable("player_wallet", "hero_chess"); e.HasKey(x => x.PlayerId); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.HasOne<Player>().WithOne().HasForeignKey<PlayerWallet>(x => x.PlayerId); });
        model.Entity<PlayerRating>(e => { e.ToTable("player_rating", "hero_chess"); e.HasKey(x => x.PlayerId); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.GamesPlayed).HasColumnName("games_played"); e.HasOne<Player>().WithOne().HasForeignKey<PlayerRating>(x => x.PlayerId); });
        model.Entity<Ruleset>(e => { Table(e, "ruleset"); e.Property(x => x.SetupBudget).HasColumnName("setup_budget"); e.Property(x => x.TurnSeconds).HasColumnName("turn_seconds"); e.Property(x => x.ActionLimit).HasColumnName("action_limit"); e.Property(x => x.IsActive).HasColumnName("is_active"); e.Property(x => x.Config).HasColumnType("jsonb"); });
        model.Entity<ChessClass>(e => { e.ToTable("chess_class", "hero_chess"); e.HasKey(x => x.Code); e.Property(x => x.NameVi).HasColumnName("name_vi"); e.Property(x => x.BaseSp).HasColumnName("base_sp"); e.Property(x => x.RequiredCount).HasColumnName("required_count"); e.Property(x => x.BaseMovementCode).HasColumnName("base_movement_code"); });
        model.Entity<LineupSlot>(e => { e.ToTable("lineup_slot", "hero_chess"); e.HasKey(x => x.SlotNo); e.Property(x => x.SlotNo).HasColumnName("slot_no"); e.Property(x => x.ClassCode).HasColumnName("class_code"); e.Property(x => x.StartX).HasColumnName("start_x"); e.Property(x => x.StartY).HasColumnName("start_y"); });
        model.Entity<HistoricalCharacter>(e => Table(e, "historical_character"));
        model.Entity<HeroTrait>(e => { Table(e, "hero_trait"); e.Property(x => x.ImplementationKey).HasColumnName("implementation_key"); e.Property(x => x.Parameters).HasColumnType("jsonb"); });
        model.Entity<Hero>(e => { Table(e, "hero"); e.Property(x => x.CharacterId).HasColumnName("character_id"); e.Property(x => x.ClassCode).HasColumnName("class_code"); e.Property(x => x.TraitId).HasColumnName("trait_id"); e.Property(x => x.SetupPoints).HasColumnName("setup_points"); e.Property(x => x.CoinPrice).HasColumnName("coin_price"); e.Property(x => x.IsStarter).HasColumnName("is_starter"); e.Property(x => x.IsEnabled).HasColumnName("is_enabled"); e.Property(x => x.IsTestFixture).HasColumnName("is_test_fixture"); e.Property(x => x.AssetKey).HasColumnName("asset_key"); e.HasOne(x => x.Trait).WithOne().HasForeignKey<Hero>(x => x.TraitId); e.HasIndex(x => x.TraitId).IsUnique().HasDatabaseName("uq_hero_trait_id"); });
        model.Entity<Faction>(e => Table(e, "faction"));
        model.Entity<HeroFaction>(e => { e.ToTable("hero_faction", "hero_chess"); e.HasKey(x => new { x.HeroId, x.FactionId }); e.Property(x => x.HeroId).HasColumnName("hero_id"); e.Property(x => x.FactionId).HasColumnName("faction_id"); });
        model.Entity<TeamSkill>(e => { Table(e, "team_skill"); e.Property(x => x.FactionId).HasColumnName("faction_id"); e.Property(x => x.ImplementationKey).HasColumnName("implementation_key"); e.Property(x => x.MaxUses).HasColumnName("max_uses"); e.Property(x => x.CooldownTurns).HasColumnName("cooldown_turns"); e.Property(x => x.IsEnabled).HasColumnName("is_enabled"); e.Property(x => x.IsTestFixture).HasColumnName("is_test_fixture"); e.Property(x => x.AssetKey).HasColumnName("asset_key"); e.Property(x => x.Parameters).HasColumnType("jsonb"); e.Property(x => x.Eligibility).HasColumnType("jsonb"); });
        model.Entity<Cosmetic>(e => { Table(e, "cosmetic"); e.Property(x => x.HeroId).HasColumnName("hero_id"); e.Property(x => x.CoinPrice).HasColumnName("coin_price"); e.Property(x => x.AssetKey).HasColumnName("asset_key"); e.Property(x => x.IsEnabled).HasColumnName("is_enabled"); });
        model.Entity<PlayerHero>(e => { e.ToTable("player_hero", "hero_chess"); e.HasKey(x => new { x.PlayerId, x.HeroId }); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.HeroId).HasColumnName("hero_id"); e.Property(x => x.AcquiredVia).HasColumnName("acquired_via"); e.Property(x => x.AcquiredAt).HasColumnName("acquired_at"); e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key"); e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId); e.HasOne<Hero>().WithMany().HasForeignKey(x => x.HeroId); });
        model.Entity<PlayerCosmetic>(e => { e.ToTable("player_cosmetic", "hero_chess"); e.HasKey(x => new { x.PlayerId, x.CosmeticId }); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.CosmeticId).HasColumnName("cosmetic_id"); e.Property(x => x.AcquiredAt).HasColumnName("acquired_at"); e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId); e.HasOne<Cosmetic>().WithMany().HasForeignKey(x => x.CosmeticId); });
        model.Entity<SavedLineup>(e => { Table(e, "lineup"); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.RulesetId).HasColumnName("ruleset_id"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); e.HasOne<Player>().WithMany().HasForeignKey(x => x.PlayerId); e.HasOne<Ruleset>().WithMany().HasForeignKey(x => x.RulesetId); e.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.LineupId); e.HasMany(x => x.Skills).WithOne().HasForeignKey(x => x.LineupId); });
        model.Entity<SavedLineupEntry>(e => { e.ToTable("lineup_entry", "hero_chess"); e.HasKey(x => new { x.LineupId, x.SlotNo }); e.Property(x => x.LineupId).HasColumnName("lineup_id"); e.Property(x => x.SlotNo).HasColumnName("slot_no"); e.Property(x => x.ClassCode).HasColumnName("class_code"); e.Property(x => x.HeroId).HasColumnName("hero_id"); e.Property(x => x.CosmeticId).HasColumnName("cosmetic_id"); });
        model.Entity<SavedLineupSkill>(e => { e.ToTable("lineup_skill", "hero_chess"); e.HasKey(x => new { x.LineupId, x.SlotNo }); e.Property(x => x.LineupId).HasColumnName("lineup_id"); e.Property(x => x.SlotNo).HasColumnName("slot_no"); e.Property(x => x.TeamSkillId).HasColumnName("team_skill_id"); });
        model.Entity<GameMatch>(e => { Table(e, "game_match"); e.Property(x => x.RulesetId).HasColumnName("ruleset_id"); e.Property(x => x.RulesetSnapshot).HasColumnName("ruleset_snapshot").HasColumnType("jsonb"); e.Property(x => x.ContentVersion).HasColumnName("content_version"); e.Property(x => x.EndReason).HasColumnName("end_reason"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.StartedAt).HasColumnName("started_at"); e.Property(x => x.EndedAt).HasColumnName("ended_at"); e.Property(x => x.SettledAt).HasColumnName("settled_at"); e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.MatchId); e.HasOne(x => x.State).WithOne().HasForeignKey<MatchState>(x => x.MatchId); });
        model.Entity<MatchParticipant>(e => { e.ToTable("match_participant", "hero_chess"); e.HasKey(x => new { x.MatchId, x.Side }); e.Property(x => x.MatchId).HasColumnName("match_id"); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.ParticipantType).HasColumnName("participant_type"); e.Property(x => x.BotConfig).HasColumnName("bot_config").HasColumnType("jsonb"); e.Property(x => x.SourceLineupId).HasColumnName("source_lineup_id"); e.Property(x => x.SourceLineupRevision).HasColumnName("source_lineup_revision"); e.Property(x => x.LineupSnapshot).HasColumnName("lineup_snapshot").HasColumnType("jsonb"); e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at"); e.Property(x => x.EloBefore).HasColumnName("elo_before"); e.Property(x => x.EloAfter).HasColumnName("elo_after"); e.Property(x => x.CoinReward).HasColumnName("coin_reward"); e.Property(x => x.Stats).HasColumnType("jsonb"); });
        model.Entity<MatchState>(e => { e.ToTable("match_state", "hero_chess"); e.HasKey(x => x.MatchId); e.Property(x => x.MatchId).HasColumnName("match_id"); e.Property(x => x.SideToMove).HasColumnName("side_to_move"); e.Property(x => x.TurnIndex).HasColumnName("turn_index"); e.Property(x => x.CountedActions).HasColumnName("counted_actions"); e.Property(x => x.TurnDeadlineAt).HasColumnName("turn_deadline_at"); e.Property(x => x.StateSchemaVersion).HasColumnName("state_schema_version"); e.Property(x => x.State).HasColumnType("jsonb"); e.Property(x => x.UpdatedAt).HasColumnName("updated_at"); });
        model.Entity<MatchAction>(e => { Table(e, "match_action"); e.Property(x => x.MatchId).HasColumnName("match_id"); e.Property(x => x.SequenceNo).HasColumnName("sequence_no"); e.Property(x => x.CommandId).HasColumnName("command_id"); e.Property(x => x.ActorSide).HasColumnName("actor_side"); e.Property(x => x.RequestPayload).HasColumnName("request_payload").HasColumnType("jsonb"); e.Property(x => x.ResolvedEvents).HasColumnName("resolved_events").HasColumnType("jsonb"); e.Property(x => x.StateAfter).HasColumnName("state_after").HasColumnType("jsonb"); e.Property(x => x.StateSchemaVersion).HasColumnName("state_schema_version"); e.Property(x => x.ReceivedAt).HasColumnName("received_at"); e.Property(x => x.CommittedAt).HasColumnName("committed_at"); });
        model.Entity<CoinTransaction>(e => { Table(e, "coin_transaction"); e.Property(x => x.PlayerId).HasColumnName("player_id"); e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key"); e.Property(x => x.BalanceAfter).HasColumnName("balance_after"); e.Property(x => x.MatchId).HasColumnName("match_id"); e.Property(x => x.MatchSide).HasColumnName("match_side"); e.Property(x => x.HeroId).HasColumnName("hero_id"); e.Property(x => x.CosmeticId).HasColumnName("cosmetic_id"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.HasOne<PlayerWallet>().WithMany().HasForeignKey(x => x.PlayerId); });
        model.Entity<AdminAuditLog>(e => { Table(e, "admin_audit_log"); e.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); e.Property(x => x.EntityType).HasColumnName("entity_type"); e.Property(x => x.EntityId).HasColumnName("entity_id"); e.Property(x => x.BeforeData).HasColumnName("before_data").HasColumnType("jsonb"); e.Property(x => x.AfterData).HasColumnName("after_data").HasColumnType("jsonb"); e.Property(x => x.CreatedAt).HasColumnName("created_at"); });
        foreach (var entity in model.Model.GetEntityTypes().Where(x => x.GetSchema() == "hero_chess"))
            foreach (var property in entity.GetProperties()) property.SetColumnName(ToSnake(property.Name));
    }

    // Table: Áp dụng quy ước cho entity có khóa Id trong schema hero_chess; bảng khóa ghép được map riêng.
    private static void Table<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e, string table) where TEntity : class
    {
        e.ToTable(table, "hero_chess");
        e.HasKey("Id");
        e.Property("Id").HasColumnName("id");
        foreach (var property in e.Metadata.GetProperties())
            if (property.GetColumnName() == property.Name) property.SetColumnName(ToSnake(property.Name));
    }

    // ToSnake: Đổi PascalCase sang snake_case để tên property khớp tên cột PostgreSQL.
    private static string ToSnake(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
