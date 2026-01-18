using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    public class TicketFlowLogVM
    {
        public long Id { get; set; }
        public long TicketId { get; set; }

        public byte? FromStatus { get; set; }
        public byte ToStatus { get; set; }
        public string Action { get; set; }

        public long OperatorId { get; set; }
        public string OperatorName { get; set; }  // 展示名：RealName(UserName)

        public string Remark { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}