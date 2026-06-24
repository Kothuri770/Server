using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;
using TrueCapture.Services;

namespace Server.Services
{
    public interface ISeparationService
    {
        Task ProcessBatchSeparationAsync(int batchId, int batchTypeId, IVerifyRepository verifyRepository, IConfigurationService configService, IBatchLogService batchLogService);
    }
}
