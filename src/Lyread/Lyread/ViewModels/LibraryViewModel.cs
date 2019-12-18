﻿using Book;
using Book.Item;
using Lyread.Annotations;
using Lyread.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Xamarin.Essentials;
using Xamarin.Forms;
using static Book.Util.BookConstant;

namespace Lyread.ViewModels
{
    public class LibraryViewModel : ListViewModel
    {
        public static IEnumerable<IPublisher> Publishers => new List<IPublisher> { new Directmedia.Directmedia(), new Duden.Duden(), new Epub.Epub() }
            .Where(p => Preferences.Get(p.GetType().Name, null) != null)
            .Where(p => new DirectoryInfo(Preferences.Get(p.GetType().Name, null)).Exists);

        public ObservableCollection<IBookItem> Books { get; }

        public RangedObservableCollection<IJobItem> Jobs { get; } = new RangedObservableCollection<IJobItem>();

        public string Pattern { get; set; }

        public int CoverWidth => Preferences.Get(nameof(SettingsViewModel.CoverSize), 3) * 24;
        public int CoverHeight => Preferences.Get(nameof(SettingsViewModel.CoverSize), 3) * 31;

        public ICommand SearchBooksCommand => new Command(Init);
        public ICommand RefreshBooksCommand { get; set; } // => CreateRefreshCommand(Init);
        public ICommand OpenBookCommand => new Command<IBookItem>(async book =>
        {
            try
            {
                await Application.Current.MainPage.Navigation.PushAsync(new BookPage(book));
            }
            catch (Exception e)
            {
                await Application.Current.MainPage.DisplayAlert("Error", e.Message, "OK");
            }
        });

        public Command LoadItemsCommand { get; set; }
        public ICommand RemainingItemsThresholdReachedCommand => new Command(async () =>
        {
            if (IsRefreshing)
                return;

            IsRefreshing = true;

            try
            {
                var items = LoadBooks();
                foreach (var item in items)
                {
                    Books.Add(item);
                }
                if (items.Count() == 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                IsRefreshing = false;
            }
        });

        public LibraryViewModel()
        {
            Books = new ObservableCollection<IBookItem>();
            LoadItemsCommand = new Command(async () => await ExecuteLoadItemsCommand());
            RefreshBooksCommand = new Command(async () =>
            {
                await ExecuteLoadItemsCommand();
                IsRefreshing = false;
            });
        }

        private IEnumerable<IBookItem> LoadBooks()
        {
            bool patternIsNullOrEmpty = string.IsNullOrEmpty(Pattern);
            int page = (Books.Count + PageSize - 1) / PageSize;
            return Publishers
                .SelectMany(p => p.QueryBooks(new DirectoryInfo(Preferences.Get(p.GetType().Name, null))))
                .Where(b => patternIsNullOrEmpty || Regex.IsMatch(b.Title, Pattern, RegexOptions.IgnoreCase))
                .Skip(page * PageSize)
                .Take(PageSize);
        }

        public void Init()
        {
            Jobs.ReplaceRange(Publishers
                .SelectMany(p => p.QueryJobs(new DirectoryInfo(Preferences.Get(p.GetType().Name, null))))
                .Select(job => new JobItem(job)));
            OnPropertyChanged(nameof(Jobs));

            Task.Run(() =>
            {
                foreach (IJobItem jobItem in Jobs)
                {
                    jobItem.Run();
                }
                Thread.Sleep(500);
                Device.BeginInvokeOnMainThread(async () =>
                {
                    Jobs.Clear();
                    OnPropertyChanged(nameof(Jobs));
                    Books.Clear();
                    LoadItemsCommand.Execute(null);
                });
            });

            OnPropertyChanged(nameof(CoverWidth));
            OnPropertyChanged(nameof(CoverHeight));
        }

        public void Clear()
        {
            Books.Clear();
        }

        async Task ExecuteLoadItemsCommand()
        {
            if (IsRefreshing)
                return;

            IsRefreshing = true;

            try
            {
                Books.Clear();
                var items = LoadBooks();
                foreach (var item in items)
                {
                    Books.Add(item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }

    public class JobItem : IJobItem, INotifyPropertyChanged
    {
        private IJobItem _wrappedJobItem;

        public JobItem(IJobItem wrappedJobItem)
        {
            _wrappedJobItem = wrappedJobItem;
        }

        public int Id => Title.GetHashCode();
        public string Title => _wrappedJobItem.Title;

        private double _progress;
        public double Progress { get { return _progress; } set { _progress = value; OnPropertyChanged(); } }

        private Color _color = Color.Black;
        public Color Color { get { return _color; } set { _color = value; OnPropertyChanged(); } }

        public bool Run()
        {
            using (Observable.FromEventPattern<ProgressEventArgs>(handler => _wrappedJobItem.ProgressChanged += handler, handler => _wrappedJobItem.ProgressChanged -= handler)
                .Sample(TimeSpan.FromMilliseconds(250))
                .Select(pattern => pattern.EventArgs.Progress)
                .Finally(() => Device.BeginInvokeOnMainThread(() => { Progress = 1; }))
                .Subscribe(progress => Device.BeginInvokeOnMainThread(() => { Progress = progress; })))
            {
                bool success = _wrappedJobItem.Run();
                Device.BeginInvokeOnMainThread(() => { Color = success ? Color.Green : Color.Red; });
                return success;
            }
        }

        public event EventHandler<ProgressEventArgs> ProgressChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    class JobItemsToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ObservableCollection<IJobItem> items)
            {
                return items.Any();
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
