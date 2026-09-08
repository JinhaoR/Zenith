using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core.Vault;

namespace Zenith.App.Settings;

public partial class VaultPanel : UserControl
{
    private VaultService? _service;
    private Action? _setup;
    private Action? _changed;
    private VaultStatus? _status;
    private VaultReview? _review;
    private long _displayedRevision = -1;
    private bool _busy;
    private bool _editingPending;
    private bool _suppressDraftChanges;
    private bool _detached;

    public VaultPanel()
    {
        InitializeComponent();
        foreach (var input in new[] { GreySeconds, GrantSeconds, VaultSeconds, SiteHost })
        {
            input.TextChanged += DraftInput_OnChanged;
        }
        foreach (var unit in new[] { GreyUnit, GrantUnit, VaultUnit })
        {
            unit.SelectionChanged += DraftInput_OnChanged;
        }
        foreach (var option in new[] { IncludeSubdomains, ChangePassword })
        {
            option.Checked += DraftInput_OnChanged;
            option.Unchecked += DraftInput_OnChanged;
        }
        NewPassword.PasswordChanged += DraftInput_OnChanged;
        RepeatPassword.PasswordChanged += DraftInput_OnChanged;
        Loaded += (_, _) => _detached = false;
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { ClearPasswords(); InvalidateReview(); }
        };
    }

    internal void Configure(VaultService? service, Action setup, Action changed)
    {
        _service = service;
        _setup = setup;
        _changed = changed;
        Refresh();
    }

    internal void Detach()
    {
        _detached = true;
        ClearPasswords();
        InvalidateReview();
    }

    internal void Refresh()
    {
        if (_busy || _detached) return;
        _status = _service?.GetStatus();
        var state = _status?.State;
        var healthy = _status?.Phase is VaultPhase.Ready or VaultPhase.Waiting or VaultPhase.Eligible;
        WorkArea.Visibility = healthy ? Visibility.Visible : Visibility.Collapsed;
        SetupButton.Visibility = _status?.Phase == VaultPhase.SetupRequired ? Visibility.Visible : Visibility.Collapsed;
        HealthText.Text = _status?.Phase switch
        {
            VaultPhase.SetupRequired => "First, configure the password shared by the Vault and temporary access. Setup does not authorize a policy change.",
            VaultPhase.ClockInvalid => "A clock change was detected. Correct the system clock and restart Zenith before changing policy.",
            VaultPhase.Unavailable or null => "Protected policy is unavailable or in use by another Zenith instance. No reset or fallback is offered here.",
            _ => "Authenticate, wait, then confirm. Your current rules remain active throughout."
        };
        ActiveSummary.Text = state is null ? "Unable to read active policy." :
            $"Greylist wait: {DurationText.Format(state.Settings.GreylistSeconds)}\nVisit duration: {DurationText.Format(state.Settings.GrantSeconds)}\nVault wait: {DurationText.Format(state.Settings.VaultSeconds)} · Revision {state.Revision}";
        TestingNotice.Visibility = state is not null && (state.Settings.GreylistSeconds == 5 || state.Settings.GrantSeconds == 5 || state.Settings.VaultSeconds == 5)
            ? Visibility.Visible : Visibility.Collapsed;
        if (state is null) return;
        if (_displayedRevision != state.Revision)
        {
            _displayedRevision = state.Revision;
            FillEditor(state.Settings, null);
            _editingPending = false;
            InvalidateReview();
        }
        PendingCard.Visibility = state.Pending is null ? Visibility.Collapsed : Visibility.Visible;
        Editor.Visibility = state.Pending is null || _editingPending ? Visibility.Visible : Visibility.Collapsed;
        if (state.Pending is { } pending)
        {
            PendingSummary.Text = Describe(state.Settings, pending.Edit);
            var remaining = pending.EligibleAt - (_status!.Now ?? pending.ProposedAt);
            Countdown.Text = remaining > TimeSpan.Zero
                ? $"Ready in {Math.Ceiling(remaining.TotalSeconds):N0} seconds · {pending.EligibleAt.ToLocalTime():G}"
                : "Ready for your confirmation";
            ConfirmButton.IsEnabled = _status.Phase == VaultPhase.Eligible;
        }
    }

    private void FillEditor(VaultSettings settings, VaultEdit? edit)
    {
        _suppressDraftChanges = true;
        try
        {
            SetDuration(GreySeconds, GreyUnit, edit?.GreylistSeconds ?? settings.GreylistSeconds);
            SetDuration(GrantSeconds, GrantUnit, edit?.GrantSeconds ?? settings.GrantSeconds);
            SetDuration(VaultSeconds, VaultUnit, edit?.VaultSeconds ?? settings.VaultSeconds);
            SiteHost.Text = edit?.AddHost ?? string.Empty;
            IncludeSubdomains.IsChecked = edit?.IncludeSubdomains == true;
            ChangePassword.IsChecked = edit?.ChangePassword == true;
            ClearPasswords();
        }
        finally { _suppressDraftChanges = false; }
    }

    private void Review_OnClick(object sender, RoutedEventArgs e)
    {
        if (_service is null || _busy || _detached) return;
        InvalidateReview();
        if (!TryDuration(GreySeconds, GreyUnit, out var grey) || !TryDuration(GrantSeconds, GrantUnit, out var grant) || !TryDuration(VaultSeconds, VaultUnit, out var vault))
        { ShowValidationError("Enter a positive whole number and choose a unit for each duration."); return; }
        try
        {
            var review = _service.Review(new(grey, grant, vault, SiteHost.Text.Trim(),
                IncludeSubdomains.IsChecked == true, ChangePassword.IsChecked == true));
            if (review.Edit.ChangePassword && (NewPassword.Password.Length is < 15 or > 128 || NewPassword.Password != RepeatPassword.Password))
            {
                ShowValidationError("Enter matching new passwords of 15–128 characters before reviewing.");
                return;
            }
            _review = review;
        }
        catch (ArgumentException exception) { ShowValidationError(exception.Message); return; }
        catch (Exception) { ShowValidationError("The active rules could not be read safely. Try again when the Vault is available."); return; }
        ReviewSummary.Text = Describe(_review.ActiveSettings, _review.Edit) + $"\nThis proposal must wait {DurationText.Format(_review.ActiveSettings.VaultSeconds)} before confirmation.";
        ReviewCard.Visibility = Visibility.Visible;
        StageButton.IsEnabled = true;
        ReviewCard.BringIntoView();
        StagePassword.Focus();
        OutcomeText.Text = string.Empty;
    }

    private async void Stage_OnClick(object sender, RoutedEventArgs e)
    {
        if (_review is not { } review || _service is null || _busy || _detached) return;
        var draft = review.Edit;
        var current = StagePassword.Password;
        var replacement = NewPassword.Password;
        var repeat = RepeatPassword.Password;
        ClearPasswords();
        InvalidateReview();
        if (draft.ChangePassword && (replacement.Length is < 15 or > 128 || replacement != repeat))
        { OutcomeText.Text = "Enter matching new passwords of 15–128 characters, then review and authenticate again."; return; }
        await ExecuteAsync(() => _service.Stage(draft, current, draft.ChangePassword ? replacement : null, review.Revision));
        current = replacement = repeat = string.Empty;
    }

    private async void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (_status?.State?.Pending is not { } pending || _service is null || _busy || _detached) return;
        var password = ConfirmPassword.Password;
        ClearPasswords();
        await ExecuteAsync(() => _service.Confirm(pending.Id, password));
        password = string.Empty;
    }

    private async void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        if (_status?.State?.Pending is not { } pending || _service is null || _busy || _detached) return;
        await ExecuteAsync(() => _service.Cancel(pending.Id));
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_status?.State is not { Pending: { } pending } state) return;
        FillEditor(state.Settings, pending.Edit);
        InvalidateReview();
        _editingPending = true;
        Refresh();
        Editor.BringIntoView();
    }

    private async Task ExecuteAsync(Func<VaultOutcome> operation)
    {
        _busy = true;
        WorkArea.IsEnabled = false;
        OutcomeText.Text = "Checking and saving securely…";
        try
        {
            var outcome = await Task.Run(operation);
            if (_detached) return;
            OutcomeText.Text = outcome.Message;
            if (outcome.Result is VaultResult.Staged or VaultResult.Applied or VaultResult.Cancelled)
            {
                _editingPending = false;
                _displayedRevision = -1;
                InvalidateReview();
                ClearPasswords();
                try { _changed?.Invoke(); }
                catch (Exception) { OutcomeText.Text = outcome.Message + " Reopen settings to refresh the display."; }
            }
        }
        catch (Exception)
        {
            if (!_detached) OutcomeText.Text = "The operation could not be completed. Review the active rules before trying again.";
        }
        finally
        {
            _busy = false;
            if (!_detached)
            {
                WorkArea.IsEnabled = true;
                Refresh();
                if (IsVisible) OutcomeText.BringIntoView();
            }
        }
    }

    private static string Describe(VaultSettings current, VaultEdit edit)
    {
        var lines = new List<string>();
        if (edit.GreylistSeconds is { } grey && grey != current.GreylistSeconds) lines.Add($"Greylist wait: {DurationText.Format(current.GreylistSeconds)} → {DurationText.Format(grey)}");
        if (edit.GrantSeconds is { } grant && grant != current.GrantSeconds) lines.Add($"Visit duration: {DurationText.Format(current.GrantSeconds)} → {DurationText.Format(grant)}");
        if (edit.VaultSeconds is { } vault && vault != current.VaultSeconds) lines.Add($"Vault wait: {DurationText.Format(current.VaultSeconds)} → {DurationText.Format(vault)}");
        if (!string.IsNullOrWhiteSpace(edit.AddHost)) lines.Add($"Add {edit.AddHost} to your Sphere ({(edit.IncludeSubdomains ? "including subdomains; Blacklist still wins" : "exact hostname only")}).");
        if (edit.ChangePassword) lines.Add("Replace the shared Vault and temporary-access password. Confirm using the old password.");
        return lines.Count == 0 ? "No changes selected." : string.Join("\n", lines);
    }

    private void ClearPasswords()
    {
        var previousSuppression = _suppressDraftChanges;
        _suppressDraftChanges = true;
        try { StagePassword.Clear(); ConfirmPassword.Clear(); NewPassword.Clear(); RepeatPassword.Clear(); }
        finally { _suppressDraftChanges = previousSuppression; }
    }

    private void DraftInput_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressDraftChanges && !_busy) InvalidateReview();
    }

    private void InvalidateReview()
    {
        _review = null;
        ReviewCard.Visibility = Visibility.Collapsed;
        StageButton.IsEnabled = false;
        StagePassword.Clear();
    }

    private void ShowValidationError(string message)
    {
        OutcomeText.Text = message;
        OutcomeText.BringIntoView();
    }
    private static void SetDuration(TextBox input, ComboBox units, int seconds)
    {
        var factor = 1;
        for (var index = units.Items.Count - 1; index >= 0; index--)
        {
            factor = int.Parse((string)((ComboBoxItem)units.Items[index]).Tag, CultureInfo.InvariantCulture);
            if (seconds % factor == 0) { units.SelectedIndex = index; break; }
        }
        input.Text = (seconds / factor).ToString(CultureInfo.InvariantCulture);
    }
    private static bool TryDuration(TextBox input, ComboBox units, out int seconds)
    {
        seconds = 0;
        if (!int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0 || units.SelectedItem is not ComboBoxItem item) return false;
        var total = (long)value * int.Parse((string)item.Tag, CultureInfo.InvariantCulture);
        if (total > int.MaxValue) return false;
        seconds = (int)total;
        return true;
    }
    private void Setup_OnClick(object sender, RoutedEventArgs e) => _setup?.Invoke();
}
