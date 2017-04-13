using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PonyMLR.DataAccess
{
    public class UnitOfWork : IDisposable
    {
        private ponydbEntities context;
        private GenericRepository<race_info> raceInfoRepository;
        private GenericRepository<race_result> raceResultRepository;
        private GenericRepository<racetrack> raceTrackRepository;
        private GenericRepository<horse_info> horseInfoRepository;
        private GenericRepository<trainer_info> trainerInfoRepository;
        private GenericRepository<jockey_info> jockeyInfoRepository;

        public string dbName;

        public UnitOfWork(string db)
        {
            context = new ponydbEntities(db);
            this.dbName = db;
        }

        public GenericRepository<race_info> RaceInfoRepository
        {
            get
            {

                if (this.raceInfoRepository == null)
                {
                    this.raceInfoRepository = new GenericRepository<race_info>(context);
                }
                return raceInfoRepository;
            }
        }

        public GenericRepository<race_result> RaceResultRepository
        {
            get
            {

                if (this.raceResultRepository == null)
                {
                    this.raceResultRepository = new GenericRepository<race_result>(context);
                }
                return raceResultRepository;
            }
        }

        public GenericRepository<racetrack> RaceTrackRepository
        {
            get
            {

                if (this.raceTrackRepository == null)
                {
                    this.raceTrackRepository = new GenericRepository<racetrack>(context);
                }
                return raceTrackRepository;
            }
        }

        public GenericRepository<horse_info> HorseInfoRepository
        {
            get
            {

                if (this.horseInfoRepository == null)
                {
                    this.horseInfoRepository = new GenericRepository<horse_info>(context);
                }
                return horseInfoRepository;
            }
        }

        public GenericRepository<trainer_info> TrainerInfoRepository
        {
            get
            {

                if (this.trainerInfoRepository == null)
                {
                    this.trainerInfoRepository = new GenericRepository<trainer_info>(context);
                }
                return trainerInfoRepository;
            }
        }

        public GenericRepository<jockey_info> JockeyInfoRepository
        {
            get
            {

                if (this.jockeyInfoRepository == null)
                {
                    this.jockeyInfoRepository = new GenericRepository<jockey_info>(context);
                }
                return jockeyInfoRepository;
            }
        }

        public void Save()
        {
            context.SaveChanges();
        }

        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    context.Dispose();
                }
            }
            this.disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}