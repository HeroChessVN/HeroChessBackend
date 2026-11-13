// Vai trò file: Service chung cho Admin: ghi audit, phân trang keyset và kiểm tra quyền fine-grained.
using System.Text.Json;
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class AdminAuditWriter(AppDbContext db, TimeProvider clock)
{
    // WriteAsync: Ghi một dòng admin_audit_log trong transaction của caller; không tự commit.
    public AdminAuditLog Write(Guid actorUserId, string action, string entityType, string entityId,
        object? before = null, object? after = null, string? reason = null)
    {
        var row = new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeData = before is null ? null : GameJson.Document(before),
            AfterData = after is null ? null : GameJson.Document(after),
            Reason = reason,
            CreatedAt = clock.GetUtcNow()
        };
        db.AdminAuditLogs.Add(row);
        return row;
    }
}

public static class AdminValidation
{
    // CoinPrice: Parse giá coin dạng chuỗi; số nguyên >= 0, chuẩn bất biến, không dấu phẩy.
    public static long ParseCoinPrice(string value)
    {
        if (!long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var price) || price < 0)
            throw new ApiException(400, "INVALID_COIN_PRICE", "coinPrice must be a non-negative integer string.");
        return price;
    }

    // SetupPoints: Luật DB hero_check = class_code = 'GENERAL' OR setup_points >= 1.
    // Không validate ở đây thì Postgres ném lỗi check ra ngoài thành 500 INTERNAL_ERROR.
    public static short RequireSetupPoints(string classCode, int setupPoints)
    {
        if (setupPoints < 0) throw new ApiException(400, "INVALID_SETUP_POINTS", "setupPoints must not be negative.");
        var value = (short)Math.Clamp(setupPoints, 0, short.MaxValue);
        if (!classCode.Equals("GENERAL", StringComparison.Ordinal) && value < 1)
            throw new ApiException(400, "INVALID_SETUP_POINTS", "Only GENERAL heroes may have setupPoints = 0; other classes require at least 1.");
        return value;
    }

    // Reason: Lý do bắt buộc cho thao tác nhạy cảm; chuẩn hóa khoảng trắng và giới hạn độ dài.
    public static string RequireReason(string? reason, string code = "REASON_REQUIRED")
    {
        var text = reason?.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new ApiException(400, code, "A reason is required for this action.");
        if (text.Length > 500) throw new ApiException(400, "REASON_TOO_LONG", "The reason must not exceed 500 characters.");
        return text;
    }

    // Name: Kiểm tra tên hiển thị 1..120 ký tự sau trim.
    public static string RequireName(string? value, int maxLength = 120)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new ApiException(400, "NAME_REQUIRED", "A name is required.");
        if (text.Length > maxLength) throw new ApiException(400, "NAME_TOO_LONG", $"The name must not exceed {maxLength} characters.");
        return text;
    }

    // Code: Kiểm tra mã định danh viết hoa, gồm chữ/số/dấu gạch.
    public static string RequireCode(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text)) throw new ApiException(400, "CODE_REQUIRED", "A code is required.");
        if (text.Length > 80) throw new ApiException(400, "CODE_TOO_LONG", "The code must not exceed 80 characters.");
        foreach (var ch in text)
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_'))
                throw new ApiException(400, "INVALID_CODE", "The code may only contain letters, digits, '-' and '_'.");
        return text;
    }
}
