using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk.Views;

public sealed partial class OnboardingWindow
{
    private void SetupStep()
    {
        var panels = new FrameworkElement[] { Step1Panel, Step2Panel, Step3Panel };
        var highlights = new Border[] { CreateStepHighlight, DockStepHighlight, StartStepHighlight };
        for (int index = 0; index < panels.Length; index++)
        {
            bool selected = index == _stepIndex;
            panels[index].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            highlights[index].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
        BackButton.Visibility = _stepIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _localizationService.T(_stepIndex == StepCount - 1 ? "Design.Intro.Start" : "Design.Intro.Next");
    }

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < 800;
        Grid.SetColumnSpan(TextHost, stacked ? 2 : 1);
        Grid.SetColumn(PreviewHost, stacked ? 0 : 1);
        Grid.SetColumnSpan(PreviewHost, stacked ? 2 : 1);
        Grid.SetRow(PreviewHost, stacked ? 1 : 0);
    }
}
