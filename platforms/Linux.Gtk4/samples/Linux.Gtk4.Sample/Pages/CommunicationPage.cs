using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Sample.Pages;

class CommunicationPage : ContentPage
{
    readonly Entry _toEntry;
    readonly Entry _subjectEntry;
    readonly Editor _bodyEditor;
    readonly Entry _phoneEntry;
    readonly Entry _smsBodyEntry;
    readonly Entry _smsRecipientEntry;
    readonly Label _statusLabel;

    public CommunicationPage()
    {
        Title = "Communication";

        _toEntry = new Entry
        {
            Placeholder = "recipient@example.com",
            Keyboard = Keyboard.Email,
            Text = "test@example.com"
        };

        _subjectEntry = new Entry
        {
            Placeholder = "Subject",
            Text = "Hello from MAUI on Linux"
        };

        _bodyEditor = new Editor
        {
            Placeholder = "Message body...",
            AutoSize = EditorAutoSizeOption.TextChanges,
            HeightRequest = 100,
            Text = "This is a test message sent from the Linux GTK4 sample."
        };

        _phoneEntry = new Entry
        {
            Placeholder = "+1 555 123 4567",
            Keyboard = Keyboard.Telephone,
            Text = "+15551234567"
        };

        _smsBodyEntry = new Entry
        {
            Placeholder = "SMS body",
            Text = "Hello from MAUI SMS on Linux!"
        };

        _smsRecipientEntry = new Entry
        {
            Placeholder = "+1 555 123 4567",
            Keyboard = Keyboard.Telephone,
            Text = "+15551234567"
        };

        _statusLabel = new Label
        {
            TextColor = Colors.Gray,
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Padding = new Thickness(24),
                Children =
                {
                    new Label
                    {
                        Text = "Communication APIs",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold
                    },

                    new Label
                    {
                        Text = "Demonstrates Email, SMS and PhoneDialer on Linux using xdg-open / xdg-mime.",
                        TextColor = Colors.Gray,
                        FontSize = 14
                    },

					// ---------- Status ----------
					BuildSectionHeader("Feature Support"),
                    _statusLabel,
                    BuildSupportStatus(),

					// ---------- Email ----------
					BuildSectionHeader("Email"),
                    new Label { Text = "To", FontAttributes = FontAttributes.Bold },
                    _toEntry,
                    new Label { Text = "Subject", FontAttributes = FontAttributes.Bold },
                    _subjectEntry,
                    new Label { Text = "Body", FontAttributes = FontAttributes.Bold },
                    _bodyEditor,

                    new Button
                    {
                        Text = "Send Plain Text Email (mailto:)",
                        BackgroundColor = Colors.DodgerBlue,
                        TextColor = Colors.White,
                        Command = new Command(async () => await ComposePlainEmailAsync())
                    },

                    new Button
                    {
                        Text = "Send HTML Email (.eml)",
                        BackgroundColor = Colors.MediumSeaGreen,
                        TextColor = Colors.White,
                        Command = new Command(async () => await ComposeHtmlEmailAsync())
                    },

                    new Button
                    {
                        Text = "Send Email with Attachment (.eml)",
                        BackgroundColor = Colors.Orchid,
                        TextColor = Colors.White,
                        Command = new Command(async () => await ComposeEmailWithAttachmentAsync())
                    },

					// ---------- Phone ----------
					BuildSectionHeader("Phone Dialer"),
                    new Label { Text = "Phone number", FontAttributes = FontAttributes.Bold },
                    _phoneEntry,

                    new Button
                    {
                        Text = "Open Dialer (tel:)",
                        BackgroundColor = Colors.Tomato,
                        TextColor = Colors.White,
                        Command = new Command(() => OpenPhoneDialer())
                    },

					// ---------- SMS ----------
					BuildSectionHeader("SMS"),
                    new Label
                    {
                        Text = "Recipient (phone)",
                        FontAttributes = FontAttributes.Bold
                    },
                    _smsRecipientEntry,
                    new Label
                    {
                        Text = "Message",
                        FontAttributes = FontAttributes.Bold
                    }, _smsBodyEntry,

                    new Button
                    {
                        Text = "Compose SMS (sms:)",
                        BackgroundColor = Colors.Gold,
                        TextColor = Colors.Black,
                        Command = new Command(async () => await ComposeSmsAsync())
                    },

					// ---------- Notes ----------
					BuildSectionHeader("Notes"),
                    new Label
                    {
                        Text = "• Plain-text emails use the mailto: scheme.\n" +
                               "• HTML bodies and attachments require a default handler for message/rfc822 (.eml).\n" +
                               "• Register a client with:\n" +
                               "  xdg-mime default org.mozilla.Thunderbird.desktop message/rfc822\n" +
                               "• SMS and tel: require a registered x-scheme-handler.",
                        FontSize = 13,
                        TextColor = Colors.Gray
                    }
                }
            }
        };

        UpdateStatus();
    }

    static Label BuildSectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 12, 0, 0)
    };

    View BuildSupportStatus()
    {
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                CreateStatusRow("Email", Email.Default.IsComposeSupported, Email.Default is Essentials.Communication.LinuxEmail email && email.HasHandler ? "(Eml supported)" : "(Eml not available)"),
                CreateStatusRow("SMS", Sms.Default.IsComposeSupported),
                CreateStatusRow("PhoneDialer", PhoneDialer.Default.IsSupported),
            }
        };
    }

    static View CreateStatusRow(string name, bool supported, string? info = null)
    {
        return new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label
                {
                    Text = supported ? "✓" : "✗",
                    TextColor = supported ? Colors.MediumSeaGreen : Colors.Tomato,
                    FontAttributes = FontAttributes.Bold,
                    WidthRequest = 24
                },
                new Label { Text = name },
                new Label
                {
                    Text = 
                    supported ? "Supported" + " " + info : "Not available",
                    TextColor = Colors.Gray,
                    FontSize = 13
                }
            }
        };
    }

    void UpdateStatus()
    {
        _statusLabel.Text = "Handlers detected via xdg-mime. " +
                            "If a feature shows as unavailable, install a suitable client and set it as default.";
    }

    async Task ComposePlainEmailAsync()
    {
        try
        {
            var message = new EmailMessage
            {
                To = [ _toEntry.Text?.Trim() ?? "" ],
                Subject = _subjectEntry.Text,
                Body = _bodyEditor.Text,
                BodyFormat = EmailBodyFormat.PlainText
            };

            await Email.Default.ComposeAsync(message);
        }
        catch (LauncherExitException ex)
        {
            await DisplayAlertAsync("Error",
                string.Join(Environment.NewLine,
                $"Launcher reported an error ({ex.ExitCode})",
                $"A mail client may still have opened the message.", ex.Message), "OK");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    async Task ComposeHtmlEmailAsync()
    {
        try
        {
            var message = new EmailMessage
            {
                To = [ _toEntry.Text?.Trim() ?? "" ],
                Subject = _subjectEntry.Text,
                Body = $"<h2>Hello from MAUI</h2><p>{_bodyEditor.Text}</p><p><em>Sent as HTML via .eml</em></p>",
                BodyFormat = EmailBodyFormat.Html
            };

            await Email.Default.ComposeAsync(message);
        }
        catch (LauncherExitException ex)
        {
            await DisplayAlertAsync("Error",
                string.Join(Environment.NewLine,
                $"Launcher reported an error ({ex.ExitCode})",
                $"A mail client may still have opened the message.", ex.Message), "OK");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    async Task ComposeEmailWithAttachmentAsync()
    {
        try
        {
            // Create a small temporary text file to attach
            var tempPath = Path.Combine(FileSystem.CacheDirectory, "maui-attachment.txt");
            await File.WriteAllTextAsync(tempPath,
                "This is a sample attachment created by the MAUI Linux Communication sample.\n");

            var message = new EmailMessage
            {
                To = [ _toEntry.Text?.Trim() ?? "" ],
                Subject = _subjectEntry.Text + " (with attachment)",
                Body = _bodyEditor.Text,
                BodyFormat = EmailBodyFormat.PlainText,
                Attachments =
                [
                    new EmailAttachment(tempPath, "text/plain")
                ]
            };

            await Email.Default.ComposeAsync(message);
        }
        catch (LauncherExitException ex)
        {
            await DisplayAlertAsync("Error",
                string.Join(Environment.NewLine,
                $"Launcher reported an error ({ex.ExitCode})",
                $"A mail client may still have opened the message.", ex.Message), "OK");
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    void OpenPhoneDialer()
    {
        try
        {
            var number = _phoneEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(number))
            {
                DisplayAlertAsync("Input required", "Please enter a phone number.", "OK");
                return;
            }

            PhoneDialer.Default.Open(number);
        }
        catch (FeatureNotSupportedException ex)
        {
            DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    async Task ComposeSmsAsync()
    {
        try
        {
            var number = _smsRecipientEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(number))
            {
                await DisplayAlertAsync("Input required", "Please enter a phone number.", "OK");
                return;
            }

            var message = new SmsMessage(_smsBodyEntry.Text, number);
            await Sms.Default.ComposeAsync(message);
        }
        catch (FeatureNotSupportedException ex)
        {
            await DisplayAlertAsync("Not Supported", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }
}