// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Foundry.Connect.Controls;

namespace Foundry.Connect.Tests.Avalonia;

public sealed class WifiPassphraseEditorTests
{
    [AvaloniaFact]
    public void Reveal_PreservesValueAndEditorFocus()
    {
        var editor = new WifiPassphraseEditor { Password = "correct horse", IsActive = true };
        var window = new Window { Content = editor };
        window.Show();
        TextBox textBox = editor.FindControl<TextBox>("PasswordEditor")!;
        Button revealButton = editor.FindControl<Button>("RevealButton")!;

        revealButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(editor.IsRevealed);
        Assert.Equal("correct horse", editor.Password);
        Assert.True(textBox.RevealPassword);
        Assert.True(textBox.IsFocused);

        editor.IsActive = false;
        Assert.False(editor.IsRevealed);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_ExecutesOnlyWhenCommandCanExecute()
    {
        var command = new RecordingCommand();
        var editor = new WifiPassphraseEditor { SubmitCommand = command };
        var window = new Window { Content = editor };
        window.Show();
        TextBox textBox = editor.FindControl<TextBox>("PasswordEditor")!;

        textBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        command.CanExecuteValue = false;
        textBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        Assert.Equal(1, command.ExecuteCalls);
        window.Close();
    }

    private sealed class RecordingCommand : ICommand
    {
        public bool CanExecuteValue { get; set; } = true;

        public int ExecuteCalls { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => CanExecuteValue;

        public void Execute(object? parameter) => ExecuteCalls++;
    }
}
