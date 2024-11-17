using System.Diagnostics;
using System.Security.Claims;
using Dapplo.Microsoft.Extensions.Hosting.WinForms;
using OpenIddict.Abstractions;
using OpenIddict.Client;

namespace WinFormsApp1;

public partial class Form1 : Form, IWinFormsShell
{
    private readonly OpenIddictClientService _service;

    public Form1(OpenIddictClientService service)
    {
        _service = service;
        InitializeComponent();
    }
    private async void LoginButton_Click(object sender, EventArgs e)
    {
        // Disable the login button to prevent concurrent authentication operations.
        LoginButton.Enabled = false;

        try
        {
            using var source = new CancellationTokenSource(delay: TimeSpan.FromSeconds(90));

            try
            {
                // Ask OpenIddict to initiate the authentication flow (typically, by starting the system browser).
                var result = await _service.ChallengeInteractivelyAsync(new()
                {
                    CancellationToken = source.Token
                });

                // Wait for the user to complete the authorization process.
                var resultAuth = await _service.AuthenticateInteractivelyAsync(new()
                {
                    CancellationToken = source.Token,
                    Nonce = result.Nonce
                });
                var token = resultAuth.BackchannelAccessToken;

                TaskDialog.ShowDialog(new TaskDialogPage
                {
                    Caption = "Authentication successful",
                    Heading = "Authentication successful",
                    Icon = TaskDialogIcon.ShieldSuccessGreenBar,
                    Text = $"Authentication successful. Token is hidden."
                });
            }

            catch (OperationCanceledException)
            {
                TaskDialog.ShowDialog(new TaskDialogPage
                {
                    Caption = "Authentication timed out",
                    Heading = "Authentication timed out",
                    Icon = TaskDialogIcon.Warning,
                    Text = "The authentication process was aborted."
                });
            }

            catch (OpenIddictExceptions.ProtocolException exception) when (exception.Error is OpenIddictConstants.Errors.AccessDenied)
            {
                TaskDialog.ShowDialog(new TaskDialogPage
                {
                    Caption = "Authorization denied",
                    Heading = "Authorization denied",
                    Icon = TaskDialogIcon.Warning,
                    Text = "The authorization was denied by the end user."
                });
            }

            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                TaskDialog.ShowDialog(new TaskDialogPage
                {
                    Caption = "Authentication failed",
                    Heading = "Authentication failed",
                    Icon = TaskDialogIcon.Error,
                    Text = "An error occurred while trying to authenticate the user."
                });
            }
        }

        finally
        {
            // Re-enable the login button to allow starting a new authentication operation.
            LoginButton.Enabled = true;
        }
    }
}