using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Windows.System;

namespace Momoka.Tests;

internal static class SearchInputTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        cases.Add(("搜索框 Delete 删除光标后一个完整字符并保留光标", () =>
        {
            var (page, field) = Focused("甲👩‍🚀乙");
            field.Select(1, 0);
            Check.True(field.Press(VirtualKey.Delete));
            Check.Equal("甲乙", field.Text);
            Check.Equal(1, field.SelectionStart);
            Check.Equal(0, field.SelectionLength);
            Check.Equal(0, page.SearchSubmissions.Count);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索框 Delete 删除选区，光标在末尾时保留已有文字", () =>
        {
            var (_, field) = Focused("甲乙丙");
            field.Select(0, 2);
            Check.True(field.Press(VirtualKey.Delete));
            Check.Equal("丙", field.Text);
            Check.Equal(0, field.SelectionStart);
            field.Select(1, 0);
            Check.True(field.Press(VirtualKey.Delete));
            Check.Equal("丙", field.Text);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索框给组字中的 Delete、修饰键和其他输入让路", () =>
        {
            var (_, field) = Focused("甲乙");
            field.BeginComposition();
            Check.False(field.Press(VirtualKey.Delete));
            field.EndComposition();
            Check.False(field.Press(VirtualKey.Back));
            Check.False(field.Press(VirtualKey.Delete, new object()));
            Native.CtrlHeld = true;
            try { Check.False(field.Press(VirtualKey.Delete)); }
            finally { Native.CtrlHeld = false; }
            field.IsReadOnly = true;
            Check.False(field.Press(VirtualKey.Delete));
            Check.Equal("甲乙", field.Text);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("组字时不提交或防抖搜索，确认后按最终文字编辑，下一次回车提交一次", () =>
        {
            var (page, field) = Focused("旧词");
            field.BeginComposition();
            Check.Equal(1, page.TypingCancellations);
            page.EditSearchFixture("xin", AutoSuggestionBoxTextChangeReason.UserInput);
            page.SubmitSearchFixture("xin");
            Check.Equal(0, page.SearchEdits.Count);
            Check.Equal(0, page.SearchSubmissions.Count);
            field.Text = "新词";
            field.EndComposition();
            Check.Equal("新词", page.SearchEdits.Single());
            Check.Equal(0, page.SearchSubmissions.Count);
            page.EditSearchFixture("新词", AutoSuggestionBoxTextChangeReason.UserInput);
            Check.Equal(1, page.SearchEdits.Count);
            page.SubmitSearchFixture("新词");
            Check.Equal("新词", page.SearchSubmissions.Single());
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索编辑忽略程序回填和过期事件，接受真实清空", () =>
        {
            var (page, _) = Focused("旧词");
            page.ViewModel.SearchText = "旧词";
            page.EditSearchFixture("回填", AutoSuggestionBoxTextChangeReason.ProgrammaticChange);
            page.EditSearchFixture("过期", AutoSuggestionBoxTextChangeReason.UserInput, current: false);
            Check.Equal(0, page.SearchEdits.Count);
            page.EditSearchFixture("", AutoSuggestionBoxTextChangeReason.UserInput);
            Check.Equal("", page.SearchEdits.Single());
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索框离焦清除组字状态，模板更换和离页解除旧编辑框", () =>
        {
            var (page, field) = Focused("甲");
            field.BeginComposition();
            field.Blur();
            Check.True(field.Press(VirtualKey.Delete));
            var next = new TextBox { Text = "乙丙" };
            page.FocusSearchFixture(next);
            Check.False(field.Press(VirtualKey.Delete));
            Check.True(next.Press(VirtualKey.Delete));
            Check.Equal("丙", next.Text);
            page.Release();
            Check.False(next.Press(VirtualKey.Delete));
            return Task.CompletedTask;
        }
        ));
    }

    private static (LibraryPage Page, TextBox Field) Focused(string text)
    {
        var page = new LibraryPage();
        var field = new TextBox { Text = text };
        page.FocusSearchFixture(field);
        return (page, field);
    }
}
