using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;
using PonyMLR.DataAccess;

namespace PonyMLR.Core.ResearchPanel
{
    public class ResearchPanelViewModel : ViewModelBase
    {
        private IEventAggregator eventaggregator;

        private string _horseName;
        private double _winRate;
        private double _placeRate;
        private double _levelStakesReturn;
        private ObservableCollection<race_result> _starterRaceForm;

        private ObservableCollection<string> _raceFilter;
        private string _selectedFilter;

        public ResearchPanelViewModel(IEventAggregator eventaggregator)
        {
            this.eventaggregator = eventaggregator;
            eventaggregator.GetEvent<ResearchPanelUpdateEvent>().Subscribe(ResearchPanelUpdate, ThreadOption.UIThread, true);

            this.SelectedFilterChangedCommand = new DelegateCommand<object>(this.SelectedFilterChanged, this.CanSelectedFilterChanged);

            this._raceFilter = new ObservableCollection<string>();
            this._raceFilter.Add(Globals.RESEARCH_FILTER_NAME_COURSE);
            this._raceFilter.Add(Globals.RESEARCH_FILTER_NAME_DISTANCE);
            this._raceFilter.Add(Globals.RESEARCH_FILTER_NAME_GOING);
            this._raceFilter.Add(Globals.RESEARCH_FILTER_NAME_CLASS);

            WinCountByRaceType = new ObservableCollection<RaceFilterModel>();
            PlaceCountByRaceType = new ObservableCollection<RaceFilterModel>();
        }

        private void ResearchPanelUpdate(List<race_result> results)
        {
            HorseName = results.Select(x => x.horse_info.horse_name).FirstOrDefault();

            WinRate = (double)results.Count(x => x.is_winner) / results.Count();
            PlaceRate = (double)results.Count(x => x.is_placer) / results.Count();
            LevelStakesReturn = (double)results.Where(x=>x.is_winner == true).Select(y=>y.odds).Sum() - results.Count(z=>z.is_winner == false);


            StarterRaceForm = null;      
            StarterRaceForm = new ObservableCollection<race_result>(results);

            RaisePropertyChangedEvent("CanDisplayResearch");

            // default
            SelectedFilter = RaceFilter[0];
            UpdateDisplayStatisticByCourse();        
        }

        private void UpdateDisplayStatisticByCourse()
        {
            var raceTypeLists = StarterRaceForm.GroupBy(p => p.race_info.racetrack.track_name)
                            .Select(g => g.ToList());

            WinCountByRaceType.Clear();
            PlaceCountByRaceType.Clear();

            foreach (var raceType in raceTypeLists)
            {
                WinCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.racetrack.track_name).FirstOrDefault(), Count = raceType.Count(y => y.is_winner == true) });
            }

            foreach (var raceType in raceTypeLists)
            {
                PlaceCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.racetrack.track_name).FirstOrDefault(), Count = raceType.Count(y => y.is_placer == true) });
            }
        }

        private void UpdateDisplayStatisticByDistance()
        {
            var raceTypeLists = StarterRaceForm.OrderBy(o=>o.race_info.race_distance).GroupBy(p => p.race_info.race_distance)
                            .Select(g => g.ToList());

            WinCountByRaceType.Clear();
            PlaceCountByRaceType.Clear();

            foreach (var raceType in raceTypeLists)
            {
                WinCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_distance).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_winner == true) });
            }

            foreach (var raceType in raceTypeLists)
            {
                PlaceCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_distance).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_placer == true) });
            }
        }

        private void UpdateDisplayStatisticByGoing()
        {
            var raceTypeLists = StarterRaceForm.OrderBy(o => o.race_info.race_going).GroupBy(p => p.race_info.race_going)
                            .Select(g => g.ToList());

            WinCountByRaceType.Clear();
            PlaceCountByRaceType.Clear();

            foreach (var raceType in raceTypeLists)
            {
                WinCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_going).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_winner == true) });
            }

            foreach (var raceType in raceTypeLists)
            {
                PlaceCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_going).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_placer == true) });
            }
        }

        private void UpdateDisplayStatisticByClass()
        {
            var raceTypeLists = StarterRaceForm.OrderBy(o=>o.race_info.race_class).GroupBy(p => p.race_info.race_class)
                            .Select(g => g.ToList());

            WinCountByRaceType.Clear();
            PlaceCountByRaceType.Clear();

            foreach (var raceType in raceTypeLists)
            {
                WinCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_class).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_winner == true) });
            }

            foreach (var raceType in raceTypeLists)
            {
                PlaceCountByRaceType.Add(new RaceFilterModel() { Item = raceType.Select(x => x.race_info.race_class).FirstOrDefault().ToString(), Count = raceType.Count(y => y.is_placer == true) });
            }
        }

        public string HorseName
        {
            get
            {
                return this._horseName;
            }
            set
            {
                if (this._horseName == value) return;
                this._horseName = value;
                RaisePropertyChangedEvent("HorseName");
            }
        }

        public double WinRate
        {
            get
            {
                return this._winRate;
            }
            set
            {
                if (this._winRate == value) return;
                this._winRate = value;
                RaisePropertyChangedEvent("WinRate");
            }
        }

        public double PlaceRate
        {
            get
            {
                return this._placeRate;
            }
            set
            {
                if (this._placeRate == value) return;
                this._placeRate = value;
                RaisePropertyChangedEvent("PlaceRate");
            }
        }

        public double LevelStakesReturn
        {
            get
            {
                return this._levelStakesReturn;
            }
            set
            {
                if (this._levelStakesReturn == value) return;
                this._levelStakesReturn = value;
                RaisePropertyChangedEvent("LevelStakesReturn");
            }
        }

        public ObservableCollection<race_result> StarterRaceForm
        {
            get
            {
                return this._starterRaceForm;
            }
            set
            {
                if (this._starterRaceForm == value) return;
                this._starterRaceForm = value;
                RaisePropertyChangedEvent("StarterRaceForm");
            }
        }

        public ObservableCollection<RaceFilterModel> WinCountByRaceType { get; set; }
        public ObservableCollection<RaceFilterModel> PlaceCountByRaceType { get; set; }

        public bool CanDisplayResearch
        {
            get { return (StarterRaceForm != null ? true : false); }
        }

#region Filtering
        public ObservableCollection<string> RaceFilter
        {
            get
            {
                return this._raceFilter;
            }
        }

        public string SelectedFilter
        {
            get
            {
                return this._selectedFilter;
            }
            set
            {
                if (this._selectedFilter == value) return;
                this._selectedFilter = value;
                RaisePropertyChangedEvent("SelectedFilter");
            }
        }

        private void SelectedFilterChanged(object arg)
        {
            if (SelectedFilter.CompareTo(Globals.RESEARCH_FILTER_NAME_COURSE) == 0)
                UpdateDisplayStatisticByCourse();
            else if (SelectedFilter.CompareTo(Globals.RESEARCH_FILTER_NAME_DISTANCE) == 0)
                UpdateDisplayStatisticByDistance();
            else if (SelectedFilter.CompareTo(Globals.RESEARCH_FILTER_NAME_GOING) == 0)
                UpdateDisplayStatisticByGoing();
            else if (SelectedFilter.CompareTo(Globals.RESEARCH_FILTER_NAME_CLASS) == 0)
                UpdateDisplayStatisticByClass();
        }

        private bool CanSelectedFilterChanged(object arg) { return true; }

        public ICommand SelectedFilterChangedCommand { get; private set; }
#endregion

    }
}
