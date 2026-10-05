using System;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class NicknameValidatorTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("a_1")]
    [InlineData("123")]
    [InlineData("_ab")]
    [InlineData("Steve")]
    [InlineData("Player_1234")]
    [InlineData("1234567890123456")] // 16 символов
    [InlineData("_abcdefghijklmn")]  // 16 символов
    public void Validate_ValidNicknames_ReturnsSuccess(string nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.True(result.IsValid);
        Assert.Equal(NicknameError.None, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Validate_EmptyOrWhitespaceOrNull_ReturnsEmptyError(string? nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.False(result.IsValid);
        Assert.Equal(NicknameError.Empty, result.Error);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("_1")]
    [InlineData("12")]
    public void Validate_TooShort_ReturnsTooShortError(string nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.False(result.IsValid);
        Assert.Equal(NicknameError.TooShort, result.Error);
    }

    [Theory]
    [InlineData("12345678901234567")] // 17 символов
    [InlineData("SuperLongNicknameExceeds16")]
    public void Validate_TooLong_ReturnsTooLongError(string nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.False(result.IsValid);
        Assert.Equal(NicknameError.TooLong, result.Error);
    }

    [Theory]
    [InlineData("Игрок")]          // Кириллица
    [InlineData("Саша123")]        // Кириллица с цифрами
    [InlineData("player-one")]     // Дефис
    [InlineData("alex 123")]       // Пробел внутри
    [InlineData("alex.dev")]       // Точка
    [InlineData("steve🔥")]        // Эмодзи
    [InlineData("steve@mine")]     // Спецсимвол @
    [InlineData("alex!")]          // Спецсимвол !
    public void Validate_InvalidCharacters_ReturnsInvalidCharsError(string nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.False(result.IsValid);
        Assert.Equal(NicknameError.InvalidChars, result.Error);
    }

    [Theory]
    [InlineData("_test")]
    [InlineData("__123__")]
    [InlineData("123abc_")]
    public void Validate_UnderscoreAndDigitsAtStart_ReturnsSuccess(string nickname)
    {
        var result = NicknameValidator.Validate(nickname);

        Assert.True(result.IsValid);
        Assert.Equal(NicknameError.None, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Invalid-Nick")]
    [InlineData("СлишкомДлинныйНикСКириллицей")]
    public async Task GameLaunchService_RejectsInvalidNickname_ThrowsInvalidOperationException(string invalidNick)
    {
        var launchService = new FabricGameLaunchService();
        var config = new LauncherConfig
        {
            Nickname = invalidNick,
            RamMb = 4096,
            GameDir = "C:\\dummy_path"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launchService.LaunchGameAsync(config));

        Assert.Contains("Игровой никнейм не соответствует правилам", ex.Message);
    }
}
