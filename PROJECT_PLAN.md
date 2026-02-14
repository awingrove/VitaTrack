# Project Plan for Family Vitamin and Supplement Tracker App

## Overview
The Family Vitamin and Supplement Tracker is an application designed to help families keep track of their vitamin and supplement intake. This app will cater to families looking for an easy way to manage their health routines, ensuring everyone in the family is staying on track with their dietary supplements.

## Technology Stack
- **Backend**: C# using ASP.NET Razor Pages
- **Database**: SQLite for data storage
- **ORM**: Dapper for data access
- **LLM Integration**: OpenAI API (or similar) for supplement search and data extraction

## Features

### Current Implementation
1. **User Authentication**
   - Register and login user accounts
   - Password recovery and management

2. **Family Management**
   - Ability to create and manage family profiles
   - Add and remove family members

3. **Vitamins and Supplements Tracking**
   - Add vitamins and supplements with details such as dosage and frequency
   - Track individual intake history by family member
   - **LLM-Powered Supplement Search**: Users describe a supplement (manufacturer and product name), and the app uses an LLM to:
     - Search for the product on the manufacturer's website
     - Extract nutritional data automatically
     - Populate supplement nutrition details (nutrients, quantities, units)
     - Apply nutritional tags for aggregate reporting

4. **Reminders**
   - Set up reminders for when to take vitamins and supplements
   - Notifications via email or in-app alerts

5. **Progress Reports**
   - View reports on vitamin and supplement intake by family member
   - Weekly and monthly summaries per family member
   - Aggregate nutrient consumption across all supplements per family member

## Database Schema

- **Users**
  - UserID (Primary Key)
  - Username
  - Password (hashed)

- **FamilyMembers**
  - FamilyMemberID (Primary Key)
  - UserID (Foreign Key)
  - Name
  - Relationship
  - Age (optional, for RDA future integration)

- **Supplements**
  - SupplementID (Primary Key)
  - FamilyMemberID (Foreign Key)
  - Name
  - Manufacturer
  - Dosage
  - Frequency
  - StartDate
  - EndDate

- **SupplementNutritionDetails**
  - NutritionDetailID (Primary Key)
  - SupplementID (Foreign Key)
  - NutrientName
  - Quantity
  - Unit (mg, mcg, IU, %)

- **NutritionTags**
  - TagID (Primary Key)
  - TagName (e.g., "Zinc", "Vitamin D", "Iron")
  - Category (e.g., "Mineral", "Vitamin")

- **SupplementNutritionDetailsTags** (Junction Table)
  - SupplementNutritionDetailsID (Foreign Key)
  - TagID (Foreign Key)

- **DailyConsumption**
  - ConsumptionID (Primary Key)
  - FamilyMemberID (Foreign Key)
  - SupplementID (Foreign Key)
  - ConsumptionDate

## Milestones

### Phase 1: Project Setup & Database (Week 1-2)
- Set up ASP.NET Razor Pages project structure
- Design and implement SQLite database schema
- Configure Dapper ORM

### Phase 2: Core Data Models & Repositories (Week 3-4)
- Create data models for all database entities
- Implement Dapper repositories for data access
- Set up dependency injection

### Phase 3: User Authentication & Family Management (Week 5-6)
- Implement user registration and login
- Create family member management pages
- Add password recovery functionality

### Phase 4: LLM-Powered Supplement Search & Entry (Week 7-8)
- Integrate LLM API (OpenAI/similar)
- Create supplement search and data extraction feature
- Implement automatic nutrition detail population
- Add tagging system for nutrients

### Phase 5: Daily Tracking & Reporting (Week 9-10)
- Create daily consumption tracking interface
- Implement progress report generation
- Add aggregate nutrient reporting by family member

### Phase 6: Reminders & Notifications (Week 11)
- Develop reminder system
- Implement email/in-app notifications

### Phase 7: Testing & Deployment (Week 12)
- Unit tests for all components
- Integration tests for database interactions
- User acceptance testing with family groups
- Deploy to Azure App Service or similar platform

## Testing
- Unit tests for all business logic
- Integration tests for database interactions with Dapper
- LLM integration tests for supplement search accuracy
- User acceptance testing with family groups
- End-to-end testing of the full workflow

## Deployment
- Host on Azure App Service or a similar cloud platform
- Use CI/CD pipeline for automated testing and deployment
- SQLite database for development; consider migration strategy for production

## Future Goals
1. **RDA Data Integration** - Pull in Recommended Dietary Allowance (RDA) data and include that in family member reports for comparative analysis
2. **LLM-Based Supplement Alternatives** - Use an LLM to find alternative supplements if one stops being produced
3. **LLM-Based Supplement Consolidation** - Use an LLM to find alternative supplements that combine current ones and reduce cost or number of tablets

## Conclusion
This project aims to facilitate a healthier lifestyle for families by simplifying the tracking of vitamin and supplement intake through an easy-to-use web application. By leveraging LLM technology for supplement discovery and nutritional data extraction, we can significantly reduce the manual data entry burden while maintaining accuracy and completeness.