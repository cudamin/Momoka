using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;

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
}
