using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FlowDesk.Models.ViewModels
{
    public class TicketDetailVM
    {
        public Tickets Ticket { get; set; }
        public Users Creator { get; set; }
        public Users Assignee { get; set; }
        public TicketCategories Category { get; set; }

        public List<TicketFlowLogs> FlowLogs { get; set; }
        public List<TicketComments> Comments { get; set; }
    }
}