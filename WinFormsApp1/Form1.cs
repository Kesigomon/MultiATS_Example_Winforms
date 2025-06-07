using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Dapplo.Microsoft.Extensions.Hosting.WinForms;
using Microsoft.AspNetCore.SignalR.Client;
using OpenIddict.Abstractions;
using OpenIddict.Client;

namespace WinFormsApp1;

public partial class Form1 : Form, IWinFormsShell
{
    private readonly OpenIddictClientService _service;
#nullable enable
    private HubConnection? _connection;
#nullable disable
    private string _token = "";
    private string _refreshToken = "";

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
            await InteractiveAuthenticateAsync(CancellationToken.None);
        }

        finally
        {
            // Re-enable the login button to allow starting a new authentication operation.
            LoginButton.Enabled = true;
        }
    }

    private async Task InteractiveAuthenticateAsync(CancellationToken cancellationToken)
    {
        using var source = new CancellationTokenSource(delay: TimeSpan.FromSeconds(90));

        try
        {
            // Ask OpenIddict to initiate the authentication flow (typically, by starting the system browser).
            var result = await _service.ChallengeInteractivelyAsync(new()
            {
                CancellationToken = source.Token,
                Scopes =
                [
                    OpenIddictConstants.Scopes.OfflineAccess
                ]
            });

            // Wait for the user to complete the authorization process.
            var resultAuth = await _service.AuthenticateInteractivelyAsync(new()
            {
                CancellationToken = source.Token,
                Nonce = result.Nonce
            });
            _token = resultAuth.BackchannelAccessToken;
            _refreshToken = resultAuth.RefreshToken;
            await ExampleSignalR(source.Token);
            TaskDialog.ShowDialog(new()
            {
                Caption = "Authentication successful",
                Heading = "Authentication successful",
                Icon = TaskDialogIcon.ShieldSuccessGreenBar,
                Text = $"Authentication successful. Token is hidden."
            });
        }

        catch (OperationCanceledException)
        {
            // タイムアウト
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Authentication timed out",
                Heading = "Authentication timed out",
                Icon = TaskDialogIcon.Warning,
                Text = "The authentication process was aborted."
            });
        }

        catch (OpenIddictExceptions.ProtocolException exception) when (exception.Error ==
                                                                       OpenIddictConstants.Errors.UnauthorizedClient)
        {
            // ログインしたユーザーがサーバーにいないか、入鋏ロールがついてない
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Authorization denied",
                Heading = "Authorization denied",
                Icon = TaskDialogIcon.Warning,
                Text = "The authorization was denied by the end user."
            });
        }
        catch (OpenIddictExceptions.ProtocolException exception) when (exception.Error ==
                                                                       OpenIddictConstants.Errors.ServerError)
        {
            // サーバーでトラブル発生
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Invalid request",
                Heading = "Invalid request",
                Icon = TaskDialogIcon.Warning,
                Text = "The authentication request was invalid."
            });
        }
        catch (Exception exception)
        {
            // 予期しないエラー
            Debug.WriteLine("An unexpected error occurred during authentication: " + exception);
            Debug.WriteLine(exception.StackTrace);
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Authentication failed",
                Heading = "Authentication failed",
                Icon = TaskDialogIcon.Error,
                Text = "An error occurred while trying to authenticate the user."
            });
        }
    }

    private async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.AuthenticateWithRefreshTokenAsync(new()
            {
                CancellationToken = cancellationToken,
                RefreshToken = _refreshToken
            });

            _token = result.AccessToken;
            _refreshToken = result.RefreshToken;
            Debug.WriteLine($"Token refreshed successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to refresh token: {ex.Message}");
            throw;
        }
    }

    private async Task ExampleSignalR(CancellationToken cancellationToken)
    {
        if (_connection != null)
        {
            await _connection.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
            _connection = null;
        }

        try
        {
            var client = new HubConnectionBuilder()
                .WithUrl($"{Program.ADDRESS}/hub/tid?access_token={_token}")
                .Build();

            await client.StartAsync(cancellationToken);
            // Refresh the token before reconnecting
            await RefreshTokenAsync(cancellationToken);
            client.Closed += async (error) =>
            {
                Debug.WriteLine($"SignalR disconnected");
                if (error == null)
                {
                    return;
                }

                Debug.WriteLine($"Error: {error.Message} {error.StackTrace}");
                var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;
                await RefreshTokenAsync(cancellationToken);
                await ExampleSignalR(cancellationToken);
            };
            _connection = client;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("Forbidden");
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during SignalR connection: {ex.Message}");
            throw;
        }
    }

    private static async Task<string> GetResourceAsync(string token, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Program.ADDRESS}/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}