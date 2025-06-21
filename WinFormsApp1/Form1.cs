using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Dapplo.Microsoft.Extensions.Hosting.WinForms;
using Microsoft.AspNetCore.SignalR.Client;
using OpenIddict.Abstractions;
using OpenIddict.Client;

namespace WinFormsApp1;

public partial class Form1 : Form, IWinFormsShell
{
    // 認証トークンの有効期限が切れる前にリフレッシュするためのマージン(接続断時、有効期限がこれよりも短い場合はリフレッシュを試みる)
    private readonly TimeSpan _renewMargin = TimeSpan.FromMinutes(1);
    private readonly OpenIddictClientService _service;
#nullable enable
    private HubConnection? _connection;
#nullable disable
    private string _token = "";
    private string _refreshToken = "";
    private DateTimeOffset _tokenExpiration = DateTimeOffset.MinValue;

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
            await InteractiveAuthenticateAsync();
            await DisposeAndStopConnectionAsync(CancellationToken.None); // 古いクライアントを破棄
            InitializeConnection(); // 新しいクライアントを初期化
            var isActionNeeded = await StartConnectionAsync(CancellationToken.None); // 新しいクライアントを開始
            if (!isActionNeeded)
            {
                SetEventHandlers(); // イベントハンドラを設定
            }
            Debug.WriteLine("Action needed after connection start.");
        }

        finally
        {
            // Re-enable the login button to allow starting a new authentication operation.
            LoginButton.Enabled = true;
        }
    }    

    // interactive認証とエラーハンドリング
    private async Task InteractiveAuthenticateAsync()
    {
        var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token;
        await InteractiveAuthenticateAsync(cancellationToken);
    }

    // interactive認証とエラーハンドリング
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
            _tokenExpiration = resultAuth.BackchannelAccessTokenExpirationDate ?? DateTimeOffset.MinValue;
            _refreshToken = resultAuth.RefreshToken;
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
        catch (OpenIddictExceptions.ProtocolException exception) when (exception.Error == OpenIddictConstants.Errors.UnauthorizedClient)
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
        catch (OpenIddictExceptions.ProtocolException exception) when (exception.Error == OpenIddictConstants.Errors.ServerError)
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

    // リフレッシュトークンフローとエラーハンドリング
    private async Task RefreshTokenWithHandlingAsync(CancellationToken cancellationToken)
    {
        var result = await _service.AuthenticateWithRefreshTokenAsync(new()
        {
            CancellationToken = cancellationToken,
            RefreshToken = _refreshToken
        });

        _token = result.AccessToken;
        _tokenExpiration = result.AccessTokenExpirationDate ?? DateTimeOffset.MinValue;
        _refreshToken = result.RefreshToken;
        Debug.WriteLine($"Token refreshed successfully");
    }

    // _connectionの破棄と停止
    private async Task DisposeAndStopConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            return;
        }
        await _connection.StopAsync(cancellationToken);
        await _connection.DisposeAsync();
        _connection = null;
    }

    // _connectionの初期化
    private void InitializeConnection()
    {
        if (_connection != null)
        {
            throw new InvalidOperationException("_connection is already initialized.");
        }
        _connection = new HubConnectionBuilder()
            .WithUrl($"{Program.ADDRESS}/hub/tid?access_token={_token}")
            .Build();
    }

    // _connectionの開始とエラーハンドリング
    private async Task<bool> StartConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("_connection is not initialized.");
        }
        // SignalR接続の開始
        try
        {
            await _connection.StartAsync(cancellationToken);
            return false;
        }
        // Todo: 再接続時、ダイアログを出して再接続するか聞く
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            // 該当Hubにアクセスするためのロールが無い
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Authentication failed",
                Heading = "Authentication failed",
                Icon = TaskDialogIcon.Error,
                Text = "Authentication is successful, but you do not have the required role to access this hub. Please check your permissions."
            });
            
            return true; // アクションが必要な場合はtrueを返す
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start SignalR connection: {ex.Message}");
            // 予期しないエラー
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "Connection failed",
                Heading = "Connection failed",
                Icon = TaskDialogIcon.Error,
                Text = "An error occurred while trying to connect to the SignalR hub."
            });
            return true; // アクションが必要な場合はtrueを返す
            
        }
    }

    // SignalR接続のイベントハンドラ設定
    private void SetEventHandlers()
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("_connection is not initialized.");
        }
        _connection.Closed += async (error) =>
        {
            Debug.WriteLine($"SignalR disconnected");
            if (error == null)
            {
                return;
            }
            Debug.WriteLine($"Error: {error.Message}");
            await TryReconnectAsync();
        };
    }

    private async Task<bool> TryReconnectOnceAsync()
    {
        bool isActionNeeded;
        // トークンが切れていない場合 かつ 切れるまで余裕がある場合はそのまま再接続
        if (_tokenExpiration > DateTimeOffset.UtcNow + _renewMargin)
        {
            Debug.WriteLine("Try reconnect with current token...");
            isActionNeeded = await StartConnectionAsync(CancellationToken.None);
            Debug.WriteLine("Reconnected with current token.");
            return isActionNeeded;
        }
        // トークンが切れていてリフレッシュトークンが有効な場合はリフレッシュ
        try
        {
            Debug.WriteLine("Try refresh token...");
            await RefreshTokenWithHandlingAsync(CancellationToken.None);
            await DisposeAndStopConnectionAsync(CancellationToken.None); // 古いクライアントを破棄
            InitializeConnection(); // 新しいクライアントを初期化
            isActionNeeded = await StartConnectionAsync(CancellationToken.None); // 新しいクライアントを開始
            if (isActionNeeded)
            {
                return true; // アクションが必要な場合はtrueを返す
            }
            SetEventHandlers(); // イベントハンドラを設定
            Debug.WriteLine("Reconnected with refreshed token.");
            return false; // アクションが必要ない場合はfalseを返す
        }
        catch (OpenIddictExceptions.ProtocolException ex) when (ex.Error == OpenIddictConstants.Errors.InvalidGrant)
        {
            // リフレッシュトークンが無効な場合
            Debug.WriteLine("Refresh token is invalid or expired.");
            TaskDialog.ShowDialog(new TaskDialogPage
            {
                Caption = "再認証が必要です",
                Heading = "Discord再認証が必要です",
                Icon = TaskDialogIcon.Warning,
                Text = "認証情報の有効期限が切れました。再度ログインしてください。"
            });
            await InteractiveAuthenticateAsync(CancellationToken.None);
            await DisposeAndStopConnectionAsync(CancellationToken.None); // 古いクライアントを破棄
            InitializeConnection(); // 新しいクライアントを初期化
            isActionNeeded = await StartConnectionAsync(CancellationToken.None); // 新しいクライアントを開始
            if (isActionNeeded)
            {
                return true; // アクションが必要な場合はtrueを返す
            }
            SetEventHandlers(); // イベントハンドラを設定
            Debug.WriteLine("Reconnected after re-authentication.");
            return false; // アクションが必要ない場合はfalseを返す
        }
    }

    private async Task TryReconnectAsync()
    {
        while (true)
        {
            try
            {
                var isActionNeeded = await TryReconnectOnceAsync();
                if (isActionNeeded)
                {
                    Debug.WriteLine("Action needed after reconnection.");
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reconnect failed: {ex.Message}");
            }
            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                Debug.WriteLine("Reconnected successfully.");
                break;
            }
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