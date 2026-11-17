// Mirrors the API's JSON. Ids are GUID strings, money is {amount, currency}, months are "YYYY-MM".

export interface Money { amount: number; currency: string; }

export type RefillStatus = 'Open' | 'Offered' | 'Escalated' | 'OnHold' | 'Committed' | 'Completed' | 'Cancelled';
export type AttemptOutcome = 'Pending' | 'Accepted' | 'Declined' | 'OfferExpired' | 'PurchaseDeadlineMissed' | 'Superseded';
export type ClaimStatus = 'Pending' | 'Submitted' | 'Approved' | 'Rejected' | 'Paid';
export type AvailabilityKind = 'Holiday' | 'Remote' | 'AtOffice';

export interface BudgetStatus {
  officeId: string;
  month: string;
  limit: Money;
  spent: Money;
  reserved: Money;
  remaining: Money;
}

export interface RefillSummary {
  id: string;
  officeId: string;
  counterId: string;
  status: RefillStatus;
  assigneeId: string | null;
  assigneeName: string | null;
  deadlineUtc: string | null;
  reportedAt: string;
  closedAt: string | null;
  amount: Money | null;
}

export interface CounterOverview { id: string; name: string; activeRefill: RefillSummary | null; }

export interface OfficeOverview {
  id: string;
  name: string;
  currency: string;
  timeZoneId: string;
  typicalMinPurchase: Money;
  typicalMaxPurchase: Money;
  budget: BudgetStatus;
  counters: CounterOverview[];
}

export interface ReportEmptyResult { refillId: string; alreadyReported: boolean; status: RefillStatus; assigneeId: string | null; }
export interface CommitResult { refillId: string; spendUpTo: Money; purchaseDeadline: string; }
export interface CompleteResult { refillId: string; claimId: string; amount: Money; overBudget: boolean; isLate: boolean; }

export interface PurchaseItem { name: string; quantity: number; category: string | null; }

export interface RefillDetails {
  summary: RefillSummary;
  officeName: string;
  counterName: string;
  reportedBy: string;
  reportedByName: string;
  cancellationReason: string | null;
  purchase: {
    buyerId: string;
    buyerName: string;
    amount: Money;
    purchasedAt: string;
    isLate: boolean;
    items: PurchaseItem[];
  } | null;
  attempts: {
    employeeId: string;
    name: string;
    kind: 'DirectedOffer' | 'Broadcast';
    outcome: AttemptOutcome;
    offeredAt: string;
    resolvedAt: string | null;
    explanation: string | null;
  }[];
  claim: ClaimSummary | null;
}

export interface ClaimSummary {
  id: string;
  refillId: string;
  employeeId: string;
  employeeName: string;
  amount: Money;
  status: ClaimStatus;
  hrClaimId: string | null;
  createdAt: string;
  submittedAt: string | null;
  paidAt: string | null;
  rejectionReason: string | null;
  isOverdue: boolean;
}

export interface Person {
  id: string;
  displayName: string;
  homeOfficeId: string;
  homeOfficeName: string;
  payrollCurrency: string;
  isVolunteer: boolean;
  isOfficeAdmin: boolean;
  chatUserId: string;
  defaultOfficeDays: string[];
}

export interface AvailabilityEntry { id: string; from: string; to: string; kind: AvailabilityKind; officeId: string | null; }

export interface OfficeReportLine {
  officeId: string;
  officeName: string;
  limit: Money;
  spent: Money;
  remaining: Money;
  percentUsed: number;
  refillCount: number;
  averagePurchase: Money | null;
  overBudgetCount: number;
  medianTimeToRefill: string | null;
  fxRateToReporting: number;
  spentInReportingCurrency: Money;
}

export interface MonthlyReport {
  month: string;
  reportingCurrency: string;
  offices: OfficeReportLine[];
  total: Money;
  ratesUsed: Record<string, number>;
}

export interface ChatReply { text: string; actions: ChatAction[]; }
export interface ChatAction { label: string; value: string; }

// Dev tools
export interface ChatFeedItem { sentAt: string; audience: { kind: 'direct' | 'channel' | 'admins'; id: string }; text: string; actions: ChatAction[]; }
export interface HrClaim {
  hrClaimId: string;
  idempotencyKey: string;
  hrEmployeeId: string;
  amount: number;
  currency: string;
  description: string;
  receiptBytes: number;
  status: 'Received' | 'Approved' | 'Rejected' | 'PaidOut';
  comment: string | null;
  receivedAt: string;
}
export interface ClockInfo { now: string; offset: string; }

export interface Problem { title?: string; status?: number; errorCode?: string; }
