using System;

namespace MayordomiaFinanciera.Web.Models.FinancialStatement
{
    public class FinancialStatementViewModel
    {
        public int Id { get; set; }
        public DateTime StatementDate { get; set; } = DateTime.Now;

        // Activos Líquidos
        public decimal LiquidCash { get; set; }
        public decimal LiquidSavings { get; set; }
        public decimal LiquidOther { get; set; }

        // Activos Inmobiliarios
        public decimal RealEstatePrimaryHome { get; set; }
        public decimal RealEstateOther { get; set; }

        // Inversiones y Bienes
        public decimal InvestmentsRetirement { get; set; }
        public decimal InvestmentsStocks { get; set; }
        public decimal VehiclesValue { get; set; }
        public decimal PersonalPropertyValue { get; set; }

        // Pasivos / Deudas
        public decimal MortgageBalance { get; set; }
        public decimal CarLoansBalance { get; set; }
        public decimal CreditCardsBalance { get; set; }
        public decimal PersonalLoansBalance { get; set; }
        public decimal OtherDebtsBalance { get; set; }

        // Totales
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal NetWorth { get; set; }
    }

    public class SaveFinancialStatementRequest
    {
        public decimal LiquidCash { get; set; }
        public decimal LiquidSavings { get; set; }
        public decimal LiquidOther { get; set; }

        public decimal RealEstatePrimaryHome { get; set; }
        public decimal RealEstateOther { get; set; }

        public decimal InvestmentsRetirement { get; set; }
        public decimal InvestmentsStocks { get; set; }
        public decimal VehiclesValue { get; set; }
        public decimal PersonalPropertyValue { get; set; }

        public decimal MortgageBalance { get; set; }
        public decimal CarLoansBalance { get; set; }
        public decimal CreditCardsBalance { get; set; }
        public decimal PersonalLoansBalance { get; set; }
        public decimal OtherDebtsBalance { get; set; }
    }
}