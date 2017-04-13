using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;

namespace PonyMLR.Core.DatabaseSelect
{
    public class DatabaseSelectViewModel : ViewModelBase
    {
        private IEventAggregator eventaggregator;

        private ObservableCollection<string> _databases;
        private string _selectedDb;

        public DatabaseSelectViewModel(IEventAggregator eventaggregator)
        {
            this.eventaggregator = eventaggregator;
         
            this.SetDatabaseCommand = new DelegateCommand<object>(this.SetDatabase, this.CanSetDatabase);
          
            this._databases = new ObservableCollection<string>();
            this._databases.Add("Local");
            this._databases.Add("Remote");
            
            //initialize selected db
            SelectedDb = Databases[0];
        }

        public ObservableCollection<string> Databases
        {
            get
            {
                return this._databases;
            }
        }

        public string SelectedDb
        {
            get
            {
                return this._selectedDb;
            }
            set
            {
                if (this._selectedDb == value) return;
                this._selectedDb = value;
                RaisePropertyChangedEvent("SelectedDb");
            }
        }

        private void SetDatabase(object arg)
        {
            //set database
            Globals.DbName = SelectedDb;

            // inform other modules on database changed
            eventaggregator.GetEvent<DatabaseSetCompletedEvent>().Publish("DatabaseSelectModule");
            eventaggregator.GetEvent<StatusBarUpdateEvent>().Publish(Globals.DbName + " database selected");
        }

        private bool CanSetDatabase(object arg)
        {
            return true;
        }

        public ICommand SetDatabaseCommand { get; private set; }
    }
}
