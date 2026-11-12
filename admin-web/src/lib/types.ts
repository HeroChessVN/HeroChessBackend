// Vai trò file: TypeScript type bám 1-1 với HeroChess.Contracts/AdminContracts.cs.
// Quy tắc: mọi field mà server khai báo `string` (tiền) giữ nguyên chuỗi; field `long` nhận number nhưng KHÔNG dùng để số học.

export interface AdminPermissionDto {
  key: string;
  description: string;
}

export interface AdminHeroDto {
  id: string;
  code: string;
  name: string;
  classCode: string;
  characterId: string | null;
  setupPoints: number;
  coinPrice: string;
  isEnabled: boolean;
  isInShop: boolean;
  isStarter: boolean;
  isTestFixture: boolean;
  assetKey: string | null;
  description: string | null;
  factionCodes: string[];
  traitId: string | null;
  traitCode: string | null;
}

export interface AdminHeroPageDto {
  items: AdminHeroDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminCosmeticDto {
  id: string;
  code: string;
  name: string;
  heroId: string;
  coinPrice: string;
  isEnabled: boolean;
  assetKey: string | null;
}

export interface AdminPriceChangeDto {
  id: string;
  actorUserId: string;
  coinPriceBefore: string;
  coinPriceAfter: string;
  reason: string | null;
  changedAt: string;
}

export interface AdminSkillDto {
  id: string;
  code: string;
  name: string;
  factionId: string | null;
  factionCode: string | null;
  implementationKey: string;
  isEnabled: boolean;
  maxUses: number | null;
  cooldownTurns: number | null;
  description: string | null;
  status: string;
}

export interface AdminHeroDetailDto {
  hero: AdminHeroDto;
  cosmetics: AdminCosmeticDto[];
  skills: AdminSkillDto[];
  ownerCount: number;
  totalRevenue: number;
  priceHistory: AdminPriceChangeDto[];
}

export interface AdminFactionDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  heroCount: number;
  skillCount: number;
}

export interface AdminRulesetDto {
  id: string;
  code: string;
  name: string;
  setupBudget: number;
  turnSeconds: number;
  actionLimit: number;
  isActive: boolean;
  config: unknown;
  matchCount: number;
  lastUsedAt: string | null;
}

export interface AdminShopHeroDto {
  hero: AdminHeroDto;
  purchasable: boolean;
  purchaseCount: number;
  lastPurchasedAt: string | null;
}

export interface AdminShopHeroPageDto {
  items: AdminShopHeroDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminPurchaseDto {
  id: string;
  playerId: string;
  playerDisplayName: string | null;
  heroId: string;
  heroCode: string | null;
  amount: number;
  createdAt: string;
}

export interface AdminPurchasePageDto {
  items: AdminPurchaseDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminPlayerDto {
  id: string;
  displayName: string;
  status: string;
  email: string | null;
  elo: number;
  gamesPlayed: number;
  wins: number;
  draws: number;
  losses: number;
  coinBalance: string;
  ownedHeroes: number;
  createdAt: string;
}

export interface AdminBatchPriceResultDto {
  results: {
    heroId: string;
    succeeded: boolean;
    coinPrice: string | null;
    errorCode: string | null;
    errorMessage: string | null;
  }[];
}

export interface AdminCoinAdjustmentDto {
  playerId: string;
  balanceBefore: string;
  balanceAfter: string;
  amount: string;
}

export interface AdminPlayerPageDto {
  items: AdminPlayerDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminOwnedHeroDto {
  heroId: string;
  heroCode: string;
  heroName: string;
  acquiredVia: string;
  acquiredAt: string;
}

export interface AdminLineupSummaryDto {
  id: string;
  name: string;
  revision: number;
  entryCount: number;
  updatedAt: string;
}

export interface AdminModerationDto {
  id: string;
  actorUserId: string;
  action: string;
  reason: string | null;
  createdAt: string;
}

export interface AdminPlayerMatchDto {
  matchId: string;
  mode: string;
  status: string;
  result: string | null;
  createdAt: string;
}

export interface AdminPlayerDetailDto {
  player: AdminPlayerDto;
  heroes: AdminOwnedHeroDto[];
  lineups: AdminLineupSummaryDto[];
  moderation: AdminModerationDto[];
  matches: AdminPlayerMatchDto[];
}

export interface AdminWalletTransactionDto {
  id: string;
  kind: string;
  amount: number;
  balanceAfter: string;
  heroId: string | null;
  matchId: string | null;
  createdAt: string;
}

export interface AdminMatchSideDto {
  side: string;
  playerId: string | null;
  displayName: string | null;
  participantType: string;
  isBot: boolean;
  difficulty: string | null;
  outcome: string | null;
  eloBefore: number | null;
  eloAfter: number | null;
  coinReward: number;
  heroCount: number;
}

export interface AdminMatchDto {
  id: string;
  mode: string;
  status: string;
  result: string | null;
  endReason: string | null;
  rulesetId: string;
  rulesetCode: string | null;
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  settledAt: string | null;
  actionCount: number;
  sides: AdminMatchSideDto[];
}

export interface AdminMatchPageDto {
  items: AdminMatchDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminMatchHeroPickDto {
  slotNo: number;
  heroId: string;
  heroCode: string | null;
  heroName: string | null;
  classCode: string;
  setupPoints: number;
  factionCode: string | null;
  startX: number;
  startY: number;
}

export interface AdminMatchSkillPickDto {
  slotNo: number;
  skillId: string;
  code: string | null;
  name: string | null;
}

export interface AdminMatchSideLineupDto {
  side: string;
  heroes: AdminMatchHeroPickDto[];
  skills: AdminMatchSkillPickDto[];
}

export interface AdminMatchDetailDto {
  match: AdminMatchDto;
  rulesetSnapshot: unknown;
  contentVersion: string;
  lineups: AdminMatchSideLineupDto[];
  hasReplay: boolean;
  firstActionAt: string | null;
}

export interface AdminMatchActionDto {
  sequenceNo: number;
  kind: string;
  actorSide: string | null;
  request: unknown;
  events: unknown;
  committedAt: string;
}

export interface AdminStatsDto {
  totalPlayers: number;
  activePlayers: number;
  disabledPlayers: number;
  totalHeroes: number;
  enabledHeroes: number;
  inShopHeroes: number;
  totalMatches: number;
  completedMatches: number;
  rankedMatches: number;
  botMatches: number;
  activeMatches: number;
  totalCoinBalance: number;
  adminCount: number;
  generatedAt: string;
}

export interface AdminMetricSampleDto {
  value: number | null;
  sampleSize: number;
}

export interface AdminHeroWinRateDto {
  heroId: string;
  heroCode: string;
  heroName: string;
  factionCode: string | null;
  picks: number;
  matches: number;
  wins: number;
  draws: number;
  losses: number;
  winRate: number | null;
  pickRate: number | null;
}

export interface AdminFactionUsageDto {
  factionCode: string;
  matches: number;
  wins: number;
  draws: number;
  losses: number;
  winRate: number | null;
}

export interface AdminSkillUsageDto {
  skillId: string;
  skillCode: string;
  skillName: string;
  matches: number;
  uses: number;
}

export interface AdminBalanceAnalyticsDto {
  mode: string;
  matchCount: number;
  playerMatchCount: number;
  firstMoveAdvantage: AdminMetricSampleDto;
  averageDurationSeconds: number | null;
  heroes: AdminHeroWinRateDto[];
  factions: AdminFactionUsageDto[];
  skills: AdminSkillUsageDto[];
}

export interface AdminBotDifficultyDto {
  difficulty: string;
  matches: number;
  wins: number;
  draws: number;
  losses: number;
  winRate: number | null;
}

export interface AdminBotRulesetDto {
  rulesetId: string;
  rulesetCode: string | null;
  matches: number;
  wins: number;
  draws: number;
  losses: number;
  winRate: number | null;
}

export interface AdminBotAnalyticsDto {
  botMatches: number;
  botWins: number;
  playerWins: number;
  draws: number;
  botWinRate: number | null;
  byDifficulty: AdminBotDifficultyDto[];
  byRuleset: AdminBotRulesetDto[];
}

export interface AdminGiftCodeDto {
  id: string;
  code: string;
  rewardKind: string;
  rewardQuantity: number;
  rewardHeroId: string | null;
  rewardCosmeticId: string | null;
  startsAt: string;
  expiresAt: string | null;
  maxRedemptions: number;
  maxPerPlayer: number;
  isActive: boolean;
  redemptionCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface AdminGiftCodePageDto {
  items: AdminGiftCodeDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminGiftCodeRedemptionDto {
  id: string;
  playerId: string;
  playerDisplayName: string | null;
  rewardKind: string;
  rewardQuantity: number;
  coinBalanceAfter: number | null;
  redeemedAt: string;
}

export interface AdminGiftCodeRedemptionPageDto {
  items: AdminGiftCodeRedemptionDto[];
  nextCursor: string | null;
  total: number;
}

export interface AdminAccountDto {
  id: string;
  displayName: string;
  email: string | null;
  status: string;
  permissions: string[];
  createdAt: string;
}

export interface AdminAccountPageDto {
  items: AdminAccountDto[];
}

export interface AdminAuditDto {
  id: string;
  actorUserId: string;
  action: string;
  entityType: string;
  entityId: string;
  beforeData: unknown;
  afterData: unknown;
  reason: string | null;
  createdAt: string;
}

export interface AdminAuditPageDto {
  items: AdminAuditDto[];
  nextCursor: string | null;
}

export interface MeDto {
  userId: string;
  displayName: string;
  role: string;
  status: string;
  playerId?: string | null;
  coinBalance?: string | null;
  elo?: number | null;
}

export interface TokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

// ---------- Request DTO ----------

export interface AdminHeroUpsertRequest {
  code?: string | null;
  name: string;
  classCode: string;
  setupPoints: number;
  coinPrice?: string | null;
  isEnabled?: boolean | null;
  isInShop?: boolean | null;
  isStarter?: boolean | null;
  assetKey?: string | null;
  description?: string | null;
  traitId?: string | null;
  reason?: string | null;
}

export interface AdminHeroUpdateRequest {
  name?: string | null;
  setupPoints?: number | null;
  isEnabled?: boolean | null;
  assetKey?: string | null;
  description?: string | null;
  traitId?: string | null;
  reason?: string | null;
}

export interface AdminHeroFactionRequest {
  factionId?: string | null;
  reason?: string | null;
}

export interface AdminShopUpdateRequest {
  coinPrice?: string | null;
  isInShop?: boolean | null;
  reason?: string | null;
}

export interface AdminBatchPriceRequest {
  items: { heroId: string; coinPrice: string }[];
  reason?: string | null;
}

export interface AdminPlayerStatusRequest {
  status: string;
  reason: string;
}

export interface AdminCoinAdjustmentRequest {
  amount: string;
  reason: string;
}

export interface AdminGrantHeroRequest {
  heroId: string;
  reason?: string | null;
}

export interface AdminGiftCodeUpsertRequest {
  code?: string | null;
  rewardKind: string;
  rewardQuantity: number;
  rewardHeroId?: string | null;
  rewardCosmeticId?: string | null;
  startsAt?: string | null;
  expiresAt?: string | null;
  maxRedemptions: number;
  maxPerPlayer: number;
  isActive?: boolean | null;
  reason?: string | null;
}

export interface AdminGiftCodeUpdateRequest {
  isActive?: boolean | null;
  expiresAt?: string | null;
  maxRedemptions?: number | null;
  maxPerPlayer?: number | null;
  reason?: string | null;
}

export interface AdminCreateAccountRequest {
  email: string;
  displayName: string;
  password: string;
  permissions?: string[] | null;
}

export interface AdminUpdatePermissionsRequest {
  permissions: string[];
}
