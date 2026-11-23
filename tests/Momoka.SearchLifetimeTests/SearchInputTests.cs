using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using Windows.System;

namespace Momoka.Tests;

internal static class SearchInputTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> cases)
    {
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

        cases.Add(("普通回车漏送按下时补搜，原生提交和长按不重复提交", () =>
        {
            var (page, field) = Focused("甲");
            Check.Equal(true, field.Release(VirtualKey.Enter));
            Check.Equal("甲", page.SearchSubmissions.Single());

            field.Text = "乙";
            field.Press(VirtualKey.Enter);
            WhileDown(VirtualKey.Enter, () =>
            {
                page.SubmitSearchFixture("乙");
                field.Press(VirtualKey.Enter);
                page.SubmitSearchFixture("乙");
            });
            Check.Equal(false, field.Release(VirtualKey.Enter));
            Check.Equal("乙", page.SearchSubmissions[1]);
            Check.Equal(2, page.SearchSubmissions.Count);

            page.SubmitSearchFixture("按钮");
            Check.Equal("按钮", page.SearchSubmissions[2]);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("回车确认组字时不会补搜，无论组字结束和键释放的先后", () =>
        {
            var (page, field) = Focused("拼音");
            field.BeginComposition();
            Check.Equal(false, field.Release(VirtualKey.Enter));
            field.EndComposition();
            Check.Equal(0, page.SearchSubmissions.Count);

            field.BeginComposition();
            field.Text = "中文";
            WhileDown(VirtualKey.Enter, () =>
            {
                field.EndComposition();
                page.SubmitSearchFixture("中文");
            });
            Check.Equal(false, field.Release(VirtualKey.Enter));
            Check.Equal(0, page.SearchSubmissions.Count);
            Check.Equal(true, field.Release(VirtualKey.Enter));
            Check.Equal("中文", page.SearchSubmissions.Single());
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索框不接管子控件回车或修饰键组合", () =>
        {
            var (page, field) = Focused("甲");
            Check.Equal(false, field.Release(VirtualKey.Enter, new Button()));
            WhileDown(VirtualKey.Control, () => field.Press(VirtualKey.Enter));
            Check.Equal(false, field.Release(VirtualKey.Enter));
            Check.Equal(0, page.SearchSubmissions.Count);
            Check.Equal(true, field.Release(VirtualKey.Enter));
            Check.Equal("甲", page.SearchSubmissions.Single());
            return Task.CompletedTask;
        }
        ));

        cases.Add(("微软拼音漏送退格按下时，抬起删除光标左边一字或选区", () =>
        {
            var (_, field) = Focused("甲👨‍👩‍👧乙");
            field.Select(field.Text.Length - 1, 0);
            Check.Equal(true, field.Release(VirtualKey.Back));
            Check.Equal("甲乙", field.Text);
            Check.Equal(1, field.SelectionStart);
            field.Select(0, 0);
            Check.Equal(false, field.Release(VirtualKey.Back));
            Check.Equal("甲乙", field.Text);
            field.Select(0, field.Text.Length);
            Check.Equal(true, field.Release(VirtualKey.Back));
            Check.Equal("", field.Text);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("原生退格、组合键和输入法组字期间不重复补删", () =>
        {
            var (_, field) = Focused("甲乙");
            field.Select(field.Text.Length, 0);
            field.Press(VirtualKey.Back);
            Check.Equal(false, field.Release(VirtualKey.Back));

            field.BeginComposition();
            Check.Equal(false, field.Release(VirtualKey.Back));
            Check.Equal("甲乙", field.Text);
            InputKeyboardSource.KeysDown.Add(VirtualKey.Back);
            try { field.EndComposition(); }
            finally { InputKeyboardSource.KeysDown.Remove(VirtualKey.Back); }
            Check.Equal(false, field.Release(VirtualKey.Back));

            InputKeyboardSource.KeysDown.Add(VirtualKey.Control);
            try { Check.Equal(false, field.Release(VirtualKey.Back)); }
            finally { InputKeyboardSource.KeysDown.Remove(VirtualKey.Control); }
            Check.Equal("甲乙", field.Text);
            Check.Equal(true, field.Release(VirtualKey.Back));
            Check.Equal("甲", field.Text);
            return Task.CompletedTask;
        }
        ));

        cases.Add(("搜索框离焦清除组字状态，模板更换和离页解除旧编辑框", () =>
        {
            var (page, field) = Focused("甲");
            field.BeginComposition();
            field.Blur();
            page.SubmitSearchFixture("甲");
            Check.Equal("甲", page.SearchSubmissions.Single());

            var next = new TextBox { Text = "乙" };
            page.FocusSearchFixture(next);
            field.BeginComposition();
            page.SubmitSearchFixture("乙");
            Check.Equal(2, page.SearchSubmissions.Count);
            next.BeginComposition();
            page.SubmitSearchFixture("乙");
            Check.Equal(2, page.SearchSubmissions.Count);
            page.Release();
            next.EndComposition();
            Check.Equal(0, page.SearchEdits.Count);
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

    private static void WhileDown(VirtualKey key, Action action)
    {
        InputKeyboardSource.KeysDown.Add(key);
        try { action(); }
        finally { InputKeyboardSource.KeysDown.Remove(key); }
    }
}
