using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shaman.Types;

namespace Shaman.Runtime
{
    public struct DataTransferProgress
    {

        public DataTransferProgress(FileSize transferredData, FileSize? total, FileSize dataPerSecond)
        {
            this.transferredData = transferredData;
            this.total = total;
            this.dataPerSecond = dataPerSecond;
        }


        private FileSize? total;
        private FileSize transferredData;
        private FileSize dataPerSecond;

        public FileSize? Total { get { return total; } }
        public FileSize TransferredData { get { return transferredData; } }
        public FileSize DataPerSecond { get { return dataPerSecond; } }

        public override string ToString()
        {
            if (total == null) return transferredData.Bytes == 0 ? "0%" : (TransferredData.ToString() + " of Unknown (" + dataPerSecond.ToString() + " / sec)");
            if (total.Value == transferredData) return "Completed.";
            return
                (int)(100 * (float)TransferredData.Bytes / (float)Total.Value.Bytes) +
                "% - " + TransferredData.ToString() + " of " + Total.Value.ToString() +
                " (" + dataPerSecond.ToString() + " / sec)";
        }

        public double? Progress
        {
            get
            {
                if (total == null) return null;
                return (double)transferredData.Bytes / total.Value.Bytes;
            }
        }


    }
}
