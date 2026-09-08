using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Zenith.Core.Access;
using Zenith.App.Settings;

namespace Zenith.App.Access;

public partial class AccessWindow : Window
{
    private readonly GreylistAccessService _service;
    private readonly Uri? _target;
    private readonly Action<Uri> _open;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AccessPhase _phase;
    private bool _busy;
    private bool _closed;

    internal AccessWindow(GreylistAccessService service, Uri? target, Action<Uri> open)
    {
        _service = service;
        _target = target;
        _open = open;
        InitializeComponent();
        HostText.Text = target?.IdnHost ?? string.Empty;
        TargetCard.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        _timer.Tick += Timer_OnTick;
        _timer.Start();
        Refresh();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        if (!_busy) { Refresh(); }
    }

    private void Refresh()
    {
        var state = _target is null
            ? new AccessStatus(_service.ConfigurationState switch
            {
                AccessConfigurationState.NeedsSetup => AccessPhase.SetupRequired,
                AccessConfigurationState.Ready => AccessPhase.Granted,
                _ => AccessPhase.Unavailable
            })
            : _service.GetStatus(_target.AbsoluteUri);
        _phase = state.Phase;
        var timing = _service.Timing;
        var wait = timing is null ? "the configured wait" : DurationText.Format(timing.CooldownSeconds);
        var duration = timing is null ? "the configured duration" : DurationText.Format(timing.GrantSeconds);
        ScopeText.Text = $"Exact hostname only · {duration} for new requests · until Zenith closes";
        ConfirmationPanel.Visibility = _phase == AccessPhase.SetupRequired ? Visibility.Visible : Visibility.Collapsed;
        PasswordPanel.Visibility = _phase is AccessPhase.SetupRequired or AccessPhase.FirstChallenge or AccessPhase.SecondChallenge
            ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.Visibility = _phase is AccessPhase.SetupRequired or AccessPhase.FirstChallenge or AccessPhase.SecondChallenge or AccessPhase.Granted
            ? Visibility.Visible : Visibility.Collapsed;
        ProgressCard.Visibility = _phase is AccessPhase.Cooldown or AccessPhase.SecondChallenge or AccessPhase.Granted
            ? Visibility.Visible : Visibility.Collapsed;

        var (heading, explanation, button) = _phase switch
        {
            AccessPhase.SetupRequired => ("Set your access password", "Create the password you will use for both challenges. Setup alone does not start a wait or grant access.", "Save password"),
            AccessPhase.FirstChallenge => ("Begin a deliberate visit", $"Enter your password to start a wait of {wait}. After that, enter the same password again to receive temporary access.", $"Start wait: {wait}"),
            AccessPhase.Cooldown => ("Give it a little time", "Your request is saved. You can keep browsing your Sphere or close Zenith; the waiting period survives a restart.", "Continue"),
            AccessPhase.SecondChallenge => ("Ready when you are", "The waiting period is complete. Enter your password again to confirm you still want this visit.", "Confirm and open"),
            AccessPhase.Granted when _target is not null => ("Your visit is ready", "Temporary access is active for this hostname until the recorded expiry or when Zenith closes.", "Open this destination"),
            AccessPhase.Granted => ("Your password is set", "When you deliberately request a site outside your Sphere, choose temporary access to begin the two-challenge process.", "Done"),
            AccessPhase.ClockInvalid => ("Check your system clock", "A clock change was detected. Temporary access is paused. Correct the system clock and restart Zenith; saved waits are retained.", "Continue"),
            AccessPhase.NotEligible => ("This request has changed", "This destination is no longer eligible for the temporary-access procedure. Return to browsing and request it again.", "Continue"),
            _ => ("Temporary access is unavailable", "Protected state could not be read or saved, or another Zenith instance is using it. Unreadable durable policy also prevents browsing. Existing credentials cannot be reset here.", "Continue")
        };
        Heading.Text = heading;
        Explanation.Text = explanation;
        SubmitButton.Content = button;
        SubmitButton.IsEnabled = !_busy && (state.RetryAfter is null || state.RetryAfter <= DateTimeOffset.UtcNow);
        if (_phase is AccessPhase.Cooldown or AccessPhase.SecondChallenge)
        {
            ProgressText.Text = _phase == AccessPhase.Cooldown ? "First challenge complete" : "Second challenge";
            TimingText.Text = $"Eligible at {state.EligibleAt?.ToLocalTime():G}. Access does not begin automatically.";
        }
        else
        {
            ProgressText.Text = _target is null ? "Password configured" : "Access active";
            TimingText.Text = state.ExpiresAt is { } expiry ? $"Ends at {expiry.ToLocalTime():G}, or when Zenith closes." : "One password, two deliberate confirmations.";
        }
    }

    private async void Submit_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) { return; }
        if (_phase == AccessPhase.Granted)
        {
            Close();
            if (_target is not null) { _open(_target); }
            return;
        }
        var password = Password.Password;
        var confirmation = Confirmation.Password;
        Password.Clear();
        Confirmation.Clear();
        ErrorText.Text = string.Empty;
        if (_phase == AccessPhase.SetupRequired && (password.Length is < 15 or > 128 || password != confirmation))
        {
            ErrorText.Text = "Enter matching passwords of 15–128 characters.";
            return;
        }
        _busy = true;
        SubmitButton.IsEnabled = false;
        PasswordPanel.IsEnabled = false;
        try
        {
            if (_phase == AccessPhase.SetupRequired)
            {
                var configured = await Task.Run(() => _service.ConfigurePassword(password));
                if (!_closed && !configured) { ErrorText.Text = "The password could not be saved. Existing credentials cannot be replaced here."; }
            }
            else if (_target is not null)
            {
                var result = await Task.Run(() => _service.SubmitPassword(_target.AbsoluteUri, password));
                if (!_closed)
                {
                    ErrorText.Text = result.Result switch
                    {
                        AccessSubmissionResult.WrongPassword => "That password did not match. Try again in five seconds.",
                        AccessSubmissionResult.RetryLater => "Please wait a few seconds before trying again.",
                        AccessSubmissionResult.Unavailable => "The request could not be completed safely. No access was issued.",
                        _ => string.Empty
                    };
                    if (result.Status.Phase == AccessPhase.Granted && result.Result == AccessSubmissionResult.Accepted)
                    {
                        Close();
                        _open(_target);
                    }
                }
            }
        }
        finally
        {
            password = string.Empty;
            confirmation = string.Empty;
            _busy = false;
            if (!_closed)
            {
                PasswordPanel.IsEnabled = true;
                Refresh();
            }
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private void Window_OnSourceInitialized(object? sender, EventArgs e) => WindowFrameAppearance.Apply(this);
    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _timer.Tick -= Timer_OnTick;
        Password.Clear();
        Confirmation.Clear();
    }
    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && SubmitButton.IsEnabled && SubmitButton.Visibility == Visibility.Visible)
        {
            Submit_OnClick(sender, e);
            e.Handled = true;
        }
    }
}
