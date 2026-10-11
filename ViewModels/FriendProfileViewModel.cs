using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;

public class FriendProfileAchievementItemViewModel : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string IconKey { get; set; } = "IconCrown";
}

public class FriendProfileWorldItemViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string LastPlayedFormatted { get; set; } = string.Empty;
    public string SizeFormatted { get; set; } = string.Empty;
}

public class FriendProfileViewModel : ObservableObject
{
    private readonly FriendItemViewModel _friend;
    private readonly IFriendService _friendService;
    private readonly ISkinService _skinService;
    private readonly IAchievementService? _achievementService;

    public string FriendId => _friend.Id;
    public string Nick => _friend.Nick;

    private ImageSource _avatar;
    public ImageSource Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    public string StatusText => _friend.StatusText;
    public bool IsOnline => _friend.Online;
    public string Status => _friend.Status;

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsContentVisible));
            }
        }
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(IsContentVisible));
            }
        }
    }

    public bool IsContentVisible => !IsLoading && !HasError;

    private bool _isStatsVisible;
    public bool IsStatsVisible
    {
        get => _isStatsVisible;
        private set => SetProperty(ref _isStatsVisible, value);
    }

    private string _playTimeFormatted = "0 ч";
    public string PlayTimeFormatted
    {
        get => _playTimeFormatted;
        private set => SetProperty(ref _playTimeFormatted, value);
    }

    private string _gameLaunchesFormatted = "0";
    public string GameLaunchesFormatted
    {
        get => _gameLaunchesFormatted;
        private set => SetProperty(ref _gameLaunchesFormatted, value);
    }

    private string _achievementsSummaryText = "—";
    public string AchievementsSummaryText
    {
        get => _achievementsSummaryText;
        private set => SetProperty(ref _achievementsSummaryText, value);
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsAchievementsTabSelected));
                OnPropertyChanged(nameof(IsWorldsTabSelected));
            }
        }
    }

    public bool IsAchievementsTabSelected
    {
        get => SelectedTabIndex == 0;
        set { if (value) SelectedTabIndex = 0; }
    }

    public bool IsWorldsTabSelected
    {
        get => SelectedTabIndex == 1;
        set { if (value) SelectedTabIndex = 1; }
    }

    private bool _areAchievementsVisible;
    public bool AreAchievementsVisible
    {
        get => _areAchievementsVisible;
        private set
        {
            if (SetProperty(ref _areAchievementsVisible, value))
            {
                OnPropertyChanged(nameof(AreAchievementsHiddenByFriend));
            }
        }
    }

    public bool AreAchievementsHiddenByFriend => !AreAchievementsVisible;

    private string _achievementsHeaderText = "ДОСТИЖЕНИЯ 0 ИЗ 14";
    public string AchievementsHeaderText
    {
        get => _achievementsHeaderText;
        private set => SetProperty(ref _achievementsHeaderText, value);
    }

    private double _achievementsProgressRatio;
    public double AchievementsProgressRatio
    {
        get => _achievementsProgressRatio;
        private set => SetProperty(ref _achievementsProgressRatio, value);
    }

    public ObservableCollection<FriendProfileAchievementItemViewModel> UnlockedAchievements { get; } = new();
    public ObservableCollection<FriendProfileAchievementItemViewModel> LockedAchievements { get; } = new();

    private bool _areWorldsVisible;
    public bool AreWorldsVisible
    {
        get => _areWorldsVisible;
        private set
        {
            if (SetProperty(ref _areWorldsVisible, value))
            {
                OnPropertyChanged(nameof(HasNoSharedWorlds));
            }
        }
    }

    public ObservableCollection<FriendProfileWorldItemViewModel> Worlds { get; } = new();

    public bool HasNoSharedWorlds => !AreWorldsVisible || Worlds.Count == 0;

    public RelayCommand RetryCommand { get; }

    public FriendProfileViewModel(
        FriendItemViewModel friend,
        IFriendService friendService,
        ISkinService skinService,
        IAchievementService? achievementService)
    {
        _friend = friend ?? throw new ArgumentNullException(nameof(friend));
        _friendService = friendService ?? throw new ArgumentNullException(nameof(friendService));
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _achievementService = achievementService;

        _avatar = _friend.Avatar;
        RetryCommand = new RelayCommand(async _ => await LoadProfileAsync());

        _friend.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FriendItemViewModel.Online) ||
                e.PropertyName == nameof(FriendItemViewModel.Status) ||
                e.PropertyName == nameof(FriendItemViewModel.LastSeen) ||
                e.PropertyName == nameof(FriendItemViewModel.StatusText))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(Status));
            }
            if (e.PropertyName == nameof(FriendItemViewModel.Avatar))
            {
                Avatar = _friend.Avatar;
            }
        };
    }

    public async Task LoadProfileAsync()
    {
        IsLoading = true;
        HasError = false;

        try
        {
            var profile = await _friendService.GetFriendProfileAsync(FriendId);
            if (profile == null)
            {
                HasError = true;
                IsLoading = false;
                return;
            }

            // Enrich skin avatar if SHA-1 is provided
            if (!string.IsNullOrWhiteSpace(profile.SkinSha1))
            {
                try
                {
                    var head = await _skinService.GetAvatarForPlayerAsync(profile.Nick);
                    if (head != null)
                    {
                        Avatar = head;
                    }
                }
                catch { }
            }

            // Stats
            bool showStats = profile.Privacy?.ShowStats ?? true;
            IsStatsVisible = showStats;

            if (showStats)
            {
                PlayTimeFormatted = FormatPlayTime(profile.PlayTimeSeconds.GetValueOrDefault());
                GameLaunchesFormatted = profile.GameLaunches.GetValueOrDefault().ToString("N0");
            }

            // Achievements
            bool showAchievements = profile.Privacy?.ShowAchievements ?? true;
            AreAchievementsVisible = showAchievements;

            var unlockedSet = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (profile.Achievements != null)
            {
                foreach (var ach in profile.Achievements)
                {
                    if (!string.IsNullOrWhiteSpace(ach.Id))
                    {
                        unlockedSet[ach.Id] = ach.UnlockedAt;
                    }
                }
            }

            var allDefinitions = _achievementService?.Definitions ?? GetFallbackDefinitions();
            int totalCount = allDefinitions.Count;
            int unlockedCount = 0;

            UnlockedAchievements.Clear();
            LockedAchievements.Clear();

            foreach (var def in allDefinitions)
            {
                bool isUnlocked = unlockedSet.TryGetValue(def.Id, out long unlockedAtMs);
                if (isUnlocked)
                {
                    unlockedCount++;
                    string dateText = unlockedAtMs > 0
                        ? $"Получено {DateTimeOffset.FromUnixTimeMilliseconds(unlockedAtMs).LocalDateTime:dd.MM.yyyy}"
                        : "Получено";

                    UnlockedAchievements.Add(new FriendProfileAchievementItemViewModel
                    {
                        Id = def.Id,
                        Title = def.Title,
                        Description = def.Description,
                        IsUnlocked = true,
                        StatusText = dateText,
                        IconKey = OverviewViewModel.GetAchievementIconKey(def.Id)
                    });
                }
                else
                {
                    LockedAchievements.Add(new FriendProfileAchievementItemViewModel
                    {
                        Id = def.Id,
                        Title = def.Title,
                        Description = def.Description,
                        IsUnlocked = false,
                        StatusText = "Не открыто",
                        IconKey = OverviewViewModel.GetAchievementIconKey(def.Id)
                    });
                }
            }

            AchievementsHeaderText = $"ПОЛУЧЕНО {unlockedCount} ИЗ {totalCount}";
            AchievementsProgressRatio = totalCount > 0 ? (double)unlockedCount / totalCount : 0.0;

            if (showAchievements)
            {
                AchievementsSummaryText = $"{unlockedCount} из {totalCount}";
            }
            else
            {
                AchievementsSummaryText = "—";
            }

            // Worlds
            bool showWorlds = profile.Privacy?.ShowWorlds ?? true;
            AreWorldsVisible = showWorlds;

            Worlds.Clear();
            if (showWorlds && profile.Worlds != null)
            {
                foreach (var w in profile.Worlds)
                {
                    if (string.IsNullOrWhiteSpace(w.Name)) continue;

                    string lastPlayed = w.LastPlayed > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(w.LastPlayed).LocalDateTime.ToString("dd.MM.yyyy HH:mm")
                        : "—";

                    Worlds.Add(new FriendProfileWorldItemViewModel
                    {
                        Name = w.Name,
                        LastPlayedFormatted = lastPlayed,
                        SizeFormatted = FormatBytes(w.SizeBytes)
                    });
                }
            }

            OnPropertyChanged(nameof(HasNoSharedWorlds));
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[FRIEND-PROFILE: ERROR] Error loading profile for {FriendId}: {ex.Message}");
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatPlayTime(long seconds)
    {
        if (seconds <= 0) return "0 ч";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours < 1)
        {
            return $"{(int)ts.TotalMinutes} мин";
        }
        return $"{(int)ts.TotalHours} ч";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 КБ";
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} КБ";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} МБ";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ";
    }

    private static List<AchievementDefinition> GetFallbackDefinitions() => new()
    {
        new AchievementDefinition { Id = "first_launch", Title = "Первый запуск", Description = "Запустить Aura Launcher в первый раз.", Target = 1 },
        new AchievementDefinition { Id = "ten_launches", Title = "Постоянный гость", Description = "Запустить лаунчер 10 раз.", Target = 10 },
        new AchievementDefinition { Id = "hundred_hours", Title = "Ветеран", Description = "Провести в игре 100 часов.", Target = 360000 },
        new AchievementDefinition { Id = "marathon", Title = "Марафон", Description = "Непрерывная сессия длительностью 4 часа.", Target = 14400 },
        new AchievementDefinition { Id = "night_shift", Title = "Ночная смена", Description = "Играть между 02:00 и 05:00 ночи.", Target = 1 },
        new AchievementDefinition { Id = "first_lobby", Title = "Хозяин дома", Description = "Создать своё первое сетевое лобби.", Target = 1 },
        new AchievementDefinition { Id = "full_table", Title = "Полный стол", Description = "Собрать 4 игроков в одном лобби.", Target = 4 },
        new AchievementDefinition { Id = "reconnected", Title = "Не сдаёмся", Description = "Успешно переподключиться к лобби после обрыва связи.", Target = 1 },
        new AchievementDefinition { Id = "first_friend", Title = "Первый контакт", Description = "Добавить первого друга в лаунчере.", Target = 1 },
        new AchievementDefinition { Id = "five_friends", Title = "Компания", Description = "Собрать список из 5 друзей.", Target = 5 },
        new AchievementDefinition { Id = "first_backup", Title = "Осторожный", Description = "Создать первую резервную копию мира.", Target = 1 },
        new AchievementDefinition { Id = "ten_backups", Title = "Архивариус", Description = "Сделать 10 резервных копий миров.", Target = 10 },
        new AchievementDefinition { Id = "photographer", Title = "Фотограф", Description = "Сделать 25 скриншотов в игре.", Target = 25 },
        new AchievementDefinition { Id = "own_style", Title = "Свой стиль", Description = "Установить кастомный скин в гардеробе.", Target = 1 }
    };
}
