using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

public partial class WaterLoginView : UserControl
{
    private readonly PageReadyGate _pageGate = new();
    private bool _adapterReady;

    public WaterLoginView()
    {
        InitializeComponent();

        WaterWebView.AdapterCreated += (_, _) =>
        {
            _adapterReady = true;
            _pageGate.MarkAdapterCreated();
            UpdateNativeVisibility();
            if (DataContext is MainViewModel { LoginBackgroundRequested: true } vm
                && _pageGate.HasCompletedNavigation)
                vm.LoginBackground = true;
        };
        WaterWebView.AdapterDestroyed += (_, _) =>
        {
            _adapterReady = false;
            WaterWebView.IsVisible = false;
            WebViewPlaceholder.IsVisible = true;
            _pageGate.Reset();
        };
        WaterWebView.NavigationStarted += (_, _) =>
        {
            _pageGate.MarkNavigating();
            if (DataContext is MainViewModel { LoginBackgroundRequested: true } vm)
                vm.LoginBackground = true;
        };
        WaterWebView.NavigationCompleted += (_, _) =>
        {
            _pageGate.MarkReady();
            if (DataContext is MainViewModel { LoginBackgroundRequested: true } vm)
                vm.LoginBackground = true;
        };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MainViewModel.WaterWebViewAccessor = () => WaterWebView;
        MainViewModel.WaterPageGate = _pageGate;
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdateNativeVisibility();
            if (vm.LoginBackgroundRequested && _pageGate.HasCompletedNavigation)
                vm.LoginBackground = true;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is MainViewModel vm) vm.PropertyChanged -= OnVmPropertyChanged;
        if (MainViewModel.WaterWebViewAccessor?.Invoke() == WaterWebView)
            MainViewModel.WaterWebViewAccessor = null;
        if (ReferenceEquals(MainViewModel.WaterPageGate, _pageGate))
            MainViewModel.WaterPageGate = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LoginBackground) or nameof(MainViewModel.LoginPageOpen))
            UpdateNativeVisibility();
    }

    private void UpdateNativeVisibility()
    {
        var background = DataContext is MainViewModel { LoginBackgroundRequested: true };
        WaterWebView.IsVisible = _adapterReady && !background;
        WebViewPlaceholder.IsVisible = !_adapterReady && !background;
    }
}
