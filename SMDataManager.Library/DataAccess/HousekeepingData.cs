using System;
using System.Linq;
using SMDataManager.Library.Internal.DataAccess;

namespace SMDataManager.Library.DataAccess
{
    public class HousekeepingData : IHousekeepingData
    {
        private readonly ISqlDataAccess _sqlDataAccess;

        public HousekeepingData(ISqlDataAccess sqlDataAccess)
        {
            _sqlDataAccess = sqlDataAccess;
        }

        public HousekeepingResult Sweep(DateTime abandonedBasketsBeforeUtc, DateTime sentMailBeforeUtc)
        {
            return _sqlDataAccess.LoadData<HousekeepingResult, dynamic>(
                "dbo.spHousekeeping_Sweep",
                new
                {
                    AbandonedBasketsBeforeUtc = abandonedBasketsBeforeUtc,
                    SentMailBeforeUtc = sentMailBeforeUtc
                },
                "SMDatabase").Single();
        }
    }
}
