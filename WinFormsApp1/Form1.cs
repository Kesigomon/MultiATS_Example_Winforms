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

    // 再接続間隔（ミリ秒）
    private const int ReconnectIntervalMs = 5000;

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
        try
        {
            // Ask OpenIddict to initiate the authentication flow (typically, by starting the system browser).
            var result = await _service.ChallengeInteractivelyAsync(new()
            {
                CancellationToken = cancellationToken,
                Scopes =
                [
                    OpenIddictConstants.Scopes.OfflineAccess
                ]
            });

            // Wait for the user to complete the authorization process.
            var resultAuth = await _service.AuthenticateInteractivelyAsync(new()
            {
                CancellationToken = cancellationToken,
                Nonce = result.Nonce
            });
            _token = resultAuth.BackchannelAccessToken;
            _refreshToken = resultAuth.RefreshToken;
            await ExampleSignalR(cancellationToken);
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

    private static bool IsTokenExpired(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return true;
        }

        var jwtToken = handler.ReadJwtToken(token);
        var expiration = jwtToken.ValidTo;

        return expiration < DateTime.UtcNow;
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

        _connection = await StartSignalRConnectionAsync(_token, cancellationToken);
    }

    // HubConnection生成・Closed設定・StartAsync共通化
    private async Task<HubConnection> StartSignalRConnectionAsync(string token, CancellationToken cancellationToken)
    {
        var client = new HubConnectionBuilder()
            .WithUrl($"{Program.ADDRESS}/hub/tid?access_token={token}")
            .Build();

        client.Closed += async (error) =>
        {
            Debug.WriteLine($"SignalR disconnected");
            _connection = await TryReconnectAsync(client);
        };

        await client.StartAsync(cancellationToken);
        return client;
    }

    private async Task<(bool, HubConnection)> TryReconnectOnceAsync(HubConnection client)
    {
        // トークンが切れていない場合はそのまま再接続
        if (!IsTokenExpired(_token))
        {
            try
            {
                Debug.WriteLine("Try reconnect with current token...");
                await client.StartAsync();
                Debug.WriteLine("Reconnected with current token.");
                return (true, client);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reconnect failed: {ex.Message}");
                return (false, client);
            }
        }
        // トークンが切れていてリフレッシュトークンが有効な場合はリフレッシュ
        else if (!IsTokenExpired(_refreshToken))
        {
            try
            {
                Debug.WriteLine("Try refresh token...");
                var refreshCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await RefreshTokenAsync(refreshCts.Token);
                var newClient = await StartSignalRConnectionAsync(_token, CancellationToken.None);
                await client.DisposeAsync(); // 古いクライアントを破棄
                Debug.WriteLine("Reconnected with refreshed token.");
                return (true, newClient);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token refresh failed: {ex.Message}");
                return (false, client);
            }
        }
        // 両方切れている場合は再認証を促す
        else
        {
            Debug.WriteLine("Both tokens expired. Prompting user to re-authenticate.");
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "再認証が必要です",
                Heading = "Discord再認証が必要です",
                Icon = TaskDialogIcon.Warning,
                Text = "認証情報の有効期限が切れました。再度ログインしてください。"
            });
            var reAuthenticateCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            if (InvokeRequired)
            {
                Invoke(() =>
                {
                    Task.Run(async () => await InteractiveAuthenticateAsync(reAuthenticateCts.Token));
                });
            }
            else
            {
                await InteractiveAuthenticateAsync(reAuthenticateCts.Token);
            }
            return (true, client); // 再認証後はループを抜ける
        }
    }

    private async Task<HubConnection> TryReconnectAsync(HubConnection client)
    {
        while (true)
        {
            var (success, newClient) = await TryReconnectOnceAsync(client);
            client = newClient;
            if (success)
                return client;
            await Task.Delay(ReconnectIntervalMs);
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