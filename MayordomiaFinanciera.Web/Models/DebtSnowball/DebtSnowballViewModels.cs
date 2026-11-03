using System;
using System.Collections.Generic;

namespace MayordomiaFinanciera.Web.Models.DebtSnowball
{
    public class DebtSnowballViewModel
    {
        public decimal ExtraMonthlyPayment { get; set; }
        public string StrategyType { get; set; } = "SNOWBALL";
        public List<UserDebtDto> Debts { get; set; } = new List<UserDebtDto>();
    }

    public class UserDebtDto
    {
        public int Id { get; set; }
        public string CreditorName { get; set; } = string.Empty;
        public decimal CurrentBalance { get; set; }
        public decimal MinimumPayment { get; set; }
        public decimal InterestRate { get; set; }
        public int SortOrder { get; set; }
    }

    public class SaveDebtRequest
    {
        public int? Id { get; set; }
        public string CreditorName { get; set; } = string.Empty;
        public decimal CurrentBalance { get; set; }
        public decimal MinimumPayment { get; set; }
        public decimal InterestRate { get; set; }
    }

    public class SaveSnowballConfigRequest
    {
        public decimal ExtraMonthlyPayment { get; set; }
        public string StrategyType { get; set; } = "SNOWBALL";
    }

    public class SnowballSimulationResult
    {
        public int TotalMonths { get; set; }
        public decimal TotalInterestPaid { get; set; }
        public decimal TotalPaid { get; set; }
        public DateTime EstimatedPayoffDate { get; set; }
        public List<DebtPayoffScheduleItem> Schedule { get; set; } = new List<DebtPayoffScheduleItem>();
    }

    public class DebtPayoffScheduleItem
    {
        public int MonthNumber { get; set; }
        public string DateLabel { get; set; } = string.Empty;
        public string CreditorName { get; set; } = string.Empty;
        public decimal PaymentAmount { get; set; }
        public decimal RemainingBalance { get; set; }
        public bool IsPaidOff { get; set; }
    }
}