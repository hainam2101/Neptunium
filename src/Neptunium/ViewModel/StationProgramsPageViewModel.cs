﻿using Crystal3.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crystal3.Navigation;
using System.Collections.ObjectModel;
using Neptunium.Model;

namespace Neptunium.ViewModel
{
    public class StationProgramsPageViewModel : UIViewModelBase
    {
        public ObservableCollection<ScheduleItem> SundayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> MondayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> TuesdayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> WednesdayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> ThursdayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> FridayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }

        public ObservableCollection<ScheduleItem> SaturdayItems
        {
            get { return GetPropertyValue<ObservableCollection<ScheduleItem>>(); }
            private set { SetPropertyValue<ObservableCollection<ScheduleItem>>(value: value); }
        }


        protected override async void OnNavigatedTo(object sender, CrystalNavigationEventArgs e)
        {
            if (e.Direction == CrystalNavigationDirection.Forward)
            {
                IsBusy = true;

                try
                {
                    await LoadScheduleAsync();
                }
                catch
                {

                }

                IsBusy = false;
            }

            base.OnNavigatedTo(sender, e);
        }

        private async Task LoadScheduleAsync()
        {
            //theres a better way to do this.
            SundayItems = new ObservableCollection<ScheduleItem>();
            MondayItems = new ObservableCollection<ScheduleItem>();
            TuesdayItems = new ObservableCollection<ScheduleItem>();
            WednesdayItems = new ObservableCollection<ScheduleItem>();
            ThursdayItems = new ObservableCollection<ScheduleItem>();
            FridayItems = new ObservableCollection<ScheduleItem>();
            SaturdayItems = new ObservableCollection<ScheduleItem>();

            var stations = await NepApp.Stations.GetStationsAsync();

            var stationsWithPrograms = stations.Where(x => x.Programs != null);
            var allPrograms = stationsWithPrograms.SelectMany(x => x.Programs).ToArray();

            foreach (var program in allPrograms)
            {
                if (program.TimeListings != null)
                {
                    foreach (var listing in program.TimeListings)
                    {
                        ScheduleItem item = new ScheduleItem();
                        item.Station = program.Station;
                        item.Day = listing.Day;
                        item.Time = listing.Time;
                        item.Program = program;

                        switch(item.Day.ToLower())
                        {
                            case "sunday":
                                SundayItems.Add(item);
                                break;
                            case "monday":
                                SundayItems.Add(item);
                                break;
                            case "tuesday":
                                SundayItems.Add(item);
                                break;
                            case "wednesday":
                                SundayItems.Add(item);
                                break;
                            case "thursday":
                                SundayItems.Add(item);
                                break;
                            case "friday":
                                SundayItems.Add(item);
                                break;
                            case "saturday":
                                SundayItems.Add(item);
                                break;
                        }
                    }
                }
            }
        }
    }
}
