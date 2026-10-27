using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Application.Leave.Services;

/// <summary>Input rules shared by the manual API and the Excel upload, so both reject exactly the same values, and the DTO mappings of the module.</summary>
public static class LeaveRules
{
    /// <summary>The server's current calendar year in IST. The only source of the year; no client value is ever used.</summary>
    public static int CurrentYear(DateTime nowIst) => nowIst.Year;

    /// <summary>Validates and trims an NSG number as typed. Returns an error message, or null when acceptable. Existence is checked separately against the personnel records.</summary>
    public static string? ValidateNsgNumber(string? raw, string label, out string normalized)
    {
        normalized = raw?.Trim() ?? string.Empty;
        if (normalized.Length == 0) return $"{label} is required.";
        if (normalized.Length > LeaveLimits.NsgNumberMaxLength) return $"{label} must not exceed {LeaveLimits.NsgNumberMaxLength} characters.";
        if (normalized.Any(char.IsControl) || normalized.Any(char.IsWhiteSpace)) return $"{label} must not contain spaces or control characters.";
        return null;
    }

    /// <summary>Validates a leave total typed as text (Excel). Returns an error message, or null with the parsed value.</summary>
    public static string? ValidateLeaveText(string? raw, string label, out int value)
    {
        value = 0;
        string text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0) return $"{label} is required.";

        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal number))
            return $"{label} must be a whole number between 0 and {LeaveLimits.MaxLeaveTaken}.";
        if (number < 0) return $"{label} must not be negative.";
        if (number != decimal.Truncate(number)) return $"{label} must be a whole number (no fractions).";
        if (number > LeaveLimits.MaxLeaveTaken) return $"{label} must not exceed {LeaveLimits.MaxLeaveTaken}.";

        value = (int)number;
        return null;
    }

    /// <summary>Validates a leave total from the JSON API.</summary>
    public static string? ValidateLeaveNumber(int? raw, string label)
    {
        if (raw == null) return $"{label} is required.";
        if (raw < 0) return $"{label} must not be negative.";
        if (raw > LeaveLimits.MaxLeaveTaken) return $"{label} must not exceed {LeaveLimits.MaxLeaveTaken}.";
        return null;
    }

    public static LeaveRecordAdminDto ToAdminDto(this PersonnelLeaveRecord r) => new()
    {
        Id = r.Id,
        NsgNumber = r.NsgNumber,
        LeaveYear = r.LeaveYear,
        LeaveTaken = r.LeaveTaken,
        CreatedAt = IstTime.FromIst(r.CreatedAtIst),
        CreatedBy = r.CreatedBy,
        UpdatedAt = IstTime.FromIst(r.UpdatedAtIst),
        UpdatedBy = r.UpdatedBy
    };

    public static MyLeaveRecordDto ToMyDto(this PersonnelLeaveRecord r) => new()
    {
        NsgNumber = r.NsgNumber,
        LeaveYear = r.LeaveYear,
        LeaveTaken = r.LeaveTaken,
        LastUpdatedAt = IstTime.FromIst(r.UpdatedAtIst)
    };

    public static LeaveHistoryDto ToDto(this PersonnelLeaveHistory h) => new()
    {
        Id = h.Id,
        NsgNumber = h.NsgNumber,
        LeaveYear = h.LeaveYear,
        OldLeaveTaken = h.OldLeaveTaken,
        NewLeaveTaken = h.NewLeaveTaken,
        Source = h.ChangeSource == LeaveChangeSource.Bulk ? "bulk" : "manual",
        UploadBatchId = h.UploadBatchId,
        ChangedBy = h.ChangedBy,
        ChangedAt = IstTime.FromIst(h.ChangedAtIst)
    };

    public static LeaveUploadBatchDto ToDto(this PersonnelLeaveUploadBatch b) => new()
    {
        Id = b.Id,
        FileName = b.FileName,
        LeaveYear = b.LeaveYear,
        UploadStatus = b.UploadStatus.ToString(),
        TotalRows = b.TotalRows,
        CreatedCount = b.CreatedCount,
        UpdatedCount = b.UpdatedCount,
        UnchangedCount = b.UnchangedCount,
        FailedCount = b.FailedCount,
        CreatedAt = IstTime.FromIst(b.CreatedAtIst),
        CreatedBy = b.CreatedBy,
        CompletedAt = b.CompletedAtIst.HasValue ? IstTime.FromIst(b.CompletedAtIst.Value) : null
    };

    public static LeaveUploadErrorDto ToDto(this PersonnelLeaveUploadError e) => new() { RowNumber = e.RowNumber, NsgNumber = e.NsgNumber, ErrorMessage = e.ErrorMessage };
}
