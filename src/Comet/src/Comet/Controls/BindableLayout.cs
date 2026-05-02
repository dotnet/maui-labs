using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Comet
{
/// <summary>
/// Wrapper for BindableLayout pattern from XAML.
/// Creates child views dynamically based on an items source and item template.
/// Use inside containers like VStack, HStack, Grid.
/// </summary>
public class BindableLayout<T> : ContainerView
{
private IEnumerable<T> _itemsSource;
private ObservableCollection<T> _observableSource;
private Func<T, View> _itemTemplate;

public IEnumerable<T> ItemsSource
{
get => _itemsSource;
set => SetItemsSource(value);
}

public Func<T, View> ItemTemplate
{
get => _itemTemplate;
set
{
_itemTemplate = value;
RebuildItems();
}
}

private void SetItemsSource(IEnumerable<T> source)
{
UnsubscribeFromObservable();
_itemsSource = source;
_observableSource = source as ObservableCollection<T>;

if (_observableSource is not null)
_observableSource.CollectionChanged += OnCollectionChanged;

RebuildItems();
}

private void UnsubscribeFromObservable()
{
if (_observableSource is not null)
_observableSource.CollectionChanged -= OnCollectionChanged;
}

private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
{
RebuildItems();
}

private void RebuildItems()
{
Clear();
if (_itemsSource is null || _itemTemplate is null)
return;

foreach (var item in _itemsSource)
{
var view = _itemTemplate(item);
if (view is not null)
Add(view);
}
}

protected override void Dispose(bool disposing)
{
if (disposing)
UnsubscribeFromObservable();
base.Dispose(disposing);
}
}

/// <summary>
/// Non-generic BindableLayout for simple scenarios.
/// </summary>
public class BindableLayout : ContainerView
{
private IEnumerable _itemsSource;
private INotifyCollectionChanged _observableSource;
private Func<object, View> _itemTemplate;

public IEnumerable ItemsSource
{
get => _itemsSource;
set => SetItemsSource(value);
}

public Func<object, View> ItemTemplate
{
get => _itemTemplate;
set
{
_itemTemplate = value;
RebuildItems();
}
}

private void SetItemsSource(IEnumerable source)
{
UnsubscribeFromObservable();
_itemsSource = source;
_observableSource = source as INotifyCollectionChanged;

if (_observableSource is not null)
_observableSource.CollectionChanged += OnCollectionChanged;

RebuildItems();
}

private void UnsubscribeFromObservable()
{
if (_observableSource is not null)
_observableSource.CollectionChanged -= OnCollectionChanged;
}

private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
{
RebuildItems();
}

private void RebuildItems()
{
Clear();
if (_itemsSource is null || _itemTemplate is null)
return;

foreach (var item in _itemsSource)
{
var view = _itemTemplate(item);
if (view is not null)
Add(view);
}
}

protected override void Dispose(bool disposing)
{
if (disposing)
UnsubscribeFromObservable();
base.Dispose(disposing);
}
}
}
