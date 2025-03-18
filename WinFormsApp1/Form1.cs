using System.Diagnostics;
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
                // タイムアウト
                TaskDialog.ShowDialog(new TaskDialogPage
                {
                    Caption = "Authentication timed out",
                    Heading = "Authentication timed out",
                    Icon = TaskDialogIcon.Warning,
                    Text = "The authentication process was aborted."
                });
            }

            catch (OpenIddictExceptions.ProtocolException exception) when (exception.Message ==
                                                                           OpenIddictConstants.Errors.AccessDenied)
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
            catch (OpenIddictExceptions.ProtocolException exception) when (exception.Message ==
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

    private static async Task ExampleSignalR(string token, CancellationToken cancellationToken)
    {
        await using var client = new HubConnectionBuilder()
            .WithUrl($"{Program.ADDRESS}/hub/train?access_token={token}")
            .WithAutomaticReconnect()
            .Build();
        try
        {
            await client.StartAsync(cancellationToken);
        }
        // 該当Hubにアクセスするためのロールが無いときのエラー 
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("Forbidden");
            return;
        }
        var resource = await client.InvokeAsync<int>("Emit", cancellationToken);
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