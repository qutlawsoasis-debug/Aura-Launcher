using System;
using System.Text.RegularExpressions;

namespace AuraLauncher.Core;

public enum NicknameError
{
    None,
    Empty,
    TooShort,
    TooLong,
    InvalidChars
}

public readonly struct NicknameValidationResult : IEquatable<NicknameValidationResult>
{
    public bool IsValid { get; }
    public NicknameError Error { get; }

    public NicknameValidationResult(bool isValid, NicknameError error)
    {
        IsValid = isValid;
        Error = error;
    }

    public static NicknameValidationResult Success => new(true, NicknameError.None);
    public static NicknameValidationResult Fail(NicknameError error) => new(false, error);

    public bool Equals(NicknameValidationResult other) => IsValid == other.IsValid && Error == other.Error;
    public override bool Equals(object? obj) => obj is NicknameValidationResult other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IsValid, (int)Error);
    public static bool operator ==(NicknameValidationResult left, NicknameValidationResult right) => left.Equals(right);
    public static bool operator !=(NicknameValidationResult left, NicknameValidationResult right) => !left.Equals(right);
}

/// <summary>
/// Валидатор никнейма игрока по правилам Minecraft: ^[A-Za-z0-9_]{3,16}$
/// Не зависит от WPF и слоя UI.
/// </summary>
public static class NicknameValidator
{
    private static readonly Regex ValidCharsRegex = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public static NicknameValidationResult Validate(string? nickname)
    {
        if (nickname == null || string.IsNullOrWhiteSpace(nickname))
        {
            return NicknameValidationResult.Fail(NicknameError.Empty);
        }

        if (nickname.Length < 3)
        {
            return NicknameValidationResult.Fail(NicknameError.TooShort);
        }

        if (nickname.Length > 16)
        {
            return NicknameValidationResult.Fail(NicknameError.TooLong);
        }

        if (!ValidCharsRegex.IsMatch(nickname))
        {
            return NicknameValidationResult.Fail(NicknameError.InvalidChars);
        }

        return NicknameValidationResult.Success;
    }
}
