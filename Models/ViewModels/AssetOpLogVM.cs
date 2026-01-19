using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    public class AssetOpLogVM
    {
        public long Id { get; set; }
        public byte OpType { get; set; }
        public byte? FromStatus { get; set; }
        public byte? ToStatus { get; set; }

        public string FromOwnerName { get; set; }
        public string ToOwnerName { get; set; }
        public string OperatorName { get; set; }

        public string Remark { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}