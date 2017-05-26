using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PonyMLR.DataAccess
{
    public interface IUnitOfWork
    {
        GenericRepository<race_info> RaceInfoRepository { get; }
        GenericRepository<race_result> RaceResultRepository { get; }
        GenericRepository<racetrack> RaceTrackRepository { get; }
        GenericRepository<horse_info> HorseInfoRepository { get; }
        GenericRepository<trainer_info> TrainerInfoRepository { get; }
        GenericRepository<jockey_info> JockeyInfoRepository { get; }
        void Save();
    }
}
