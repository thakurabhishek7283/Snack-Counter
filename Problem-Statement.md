# Snack Counter Automation

## The situation

We have two offices, one in **Oslo** and one in **Bengaluru**. Each has its own snack counter, kept stocked by a group of volunteers: about **15 in Oslo** and about **20 in Bengaluru**.

When a box is empty, a volunteer buys snacks with their own money, refills it, and submits the receipt to the company's HR system to get reimbursed.

Today this runs on goodwill and informal messages, and it is breaking down:
* The box sits empty for days because everyone assumes someone else will refill it.
* Sometimes two people buy snacks on the same day.
* A few people do most of the refills and wait weeks to get their money back.
* Nobody knows how much is spent per month, or which snacks actually get eaten.

## Your task

Automate this process. You decide what "automate" means, but justify your choices.

## What you know

* The HR system is run by another company. Other systems can create reimbursement claims in it by sending the amount, the currency, a description, a receipt image, and the employee's ID.
* HR approves claims, which takes **1 to 10 days**.
* Employees are reimbursed in the currency of their payroll.

### Monthly budgets

| Office | Monthly budget | Typical purchase |
| :--- | :--- | :--- |
| Oslo | 6 000 NOK | 300 – 1 200 NOK |
| Bengaluru | 15 000 INR | 500 – 3 000 INR |

* Management wants one monthly report of total snack spending across both offices.
* Employees sometimes visit the other office for a week or two.
* Volunteers work hybrid: some days at the office, some at home, sometimes on holiday.
* Every employee has a company login and uses the company's internal chat tool.

## What to deliver

1. **A short design document (at most 1–2 pages)** covering:
   * How a refill moves from "empty" to "reimbursed"
   * The main parts of the solution and the data each part keeps
   * How you prevent the problems listed above
   * The trade-offs you made, and what you would deliberately not build
2. **Working core logic**, in any language you choose, with simulated stand-ins for the chat tool and the HR system:
   * The refill lifecycle
   * Choosing who buys
   * The budget check
3. **Automated tests** that prove the important rules, for example:
   * Two people cannot both commit to the same refill
   * A refill nobody completes is handed to someone else
   * Budgets and totals are correct across both currencies

## Good to know

* Use the AI assistant as much as you like. You should be able to explain everything you hand in.
* Before you start, ask us anything you need to know. Asking questions is part of the exercise.
* You are not expected to finish everything. Clear reasoning matters more than how much you complete.
* In the last 10 minutes you will walk us through your design, code, and tests.
