# AcceptanceTestsWebApp

Blazor web application for APSIM product AcceptanceTests workflows.

This project provides:

- A public AcceptanceTests experience for APSIM products.
- Guided licence-pathway selection for general and special use AcceptanceTests.
- An admin page for viewing sample AcceptanceTests records.

## Tech Stack

- **.NET 10** (`net10.0`)
- **ASP.NET Core Razor Components** (Blazor Server with interactive server render mode)
- **Bootstrap 5** - Responsive CSS framework
- **Entity Framework Core 10** - Database access (via WebAPI)
- **Custom Components** - Reusable Razor components

## Getting Started

### Prerequisites

- .NET SDK 10.0 (preview, matching `TargetFramework: net10.0`)

### Run Locally

From the repository root:

```bash
cd AcceptanceTestsWebApp
dotnet watch run
```

Then open the URL shown in terminal output (typically `https://localhost:5012`).

### Using VS Code Task

1. Open VS Code in the repository
2. Select **Terminal** > **Run Task...**
3. Choose **Just Watch AcceptanceTestsWebApp** for hot reload during development

### Build

```bash
cd AcceptanceTestsWebApp
dotnet build
```

### Docker

```bash
docker build -f AcceptanceTestsWebApp/Dockerfile -t apsim-AcceptanceTests-webapp .
docker run -p 8090:8090 apsim-AcceptanceTests-webapp
```

## Application Routes & Pages

### Page Structure

| Route | Page Component | Purpose |
|-------|---|----------|
| `/` | Home.razor | Product selection and introduction |
| `/register/{productId?}` | Register.razor | General use AcceptanceTests form |
| `/special` | SpecialAcceptanceTests.razor | Special use license AcceptanceTests |
| `/download` | Download.razor | Download APSIM with access control |
| `/validate` | Validate.razor | Validate and download APSIM Classic |
| `/admin` | Admin.razor | Admin dashboard and AcceptanceTests viewer |
| `/error` | Error.razor | Error display page |
| (404) | NotFound.razor | Page not found handler |

## Project Structure

### Hierarchy Diagram

```
AcceptanceTestsWebApp/
├── Components/
│   ├── Pages/                      (Routable Pages)
│   │   ├── Home.razor              - Product overview with intro cards
│   │   ├── Register.razor          - General use AcceptanceTests form
│   │   ├── SpecialAcceptanceTests.razor - Special use AcceptanceTests form
│   │   ├── Download.razor          - Download interface
│   │   ├── Validate.razor          - APSIM Classic validation
│   │   ├── Admin.razor             - Admin dashboard
│   │   ├── Error.razor             - Error page
│   │   └── NotFound.razor          - 404 page
│   │
│   ├── Layout/                     (Layout Components)
│   │   ├── MainLayout.razor        - Primary layout wrapper
│   │   ├── MainLayout.razor.css    - Layout styles
│   │   ├── NavMenu.razor           - Navigation menu
│   │   ├── NavMenu.razor.css       - Nav menu styles
│   │   ├── Footer.razor            - Footer component
│   │   ├── Footer.razor.css        - Footer styles
│   │   ├── ReconnectModal.razor    - Connection loss modal
│   │   ├── ReconnectModal.razor.css - Modal styles
│   │   ├── ReconnectModal.razor.js - Modal interactivity
│   │   │
│   │   └── CustomLayout/           (Custom Layout Components)
│   │       ├── Card.razor          - Basic card component
│   │       ├── Card.razor.css
│   │       ├── ProductBox.razor    - Product display card
│   │       ├── ProductBox.razor.css
│   │       ├── DecisionCard.razor  - Decision UI card
│   │       ├── DecisionCard.razor.css
│   │       ├── DotPointCard.razor  - Bullet point card
│   │       ├── DotPointCard.razor.css
│   │       ├── CustomModal.razor   - Custom modal dialog
│   │       ├── PdfViewer.razor     - PDF display component
│   │       ├── PdfViewer.razor.css
│   │       └── BootstrapHelpers/   (Bootstrap utility components)
│   │
│   ├── LayoutObjects/              (Page-Level Objects)
│   │   └── Product.cs              - Product model for display
│   │
│   ├── Utilities/                  (Service Classes)
│   │   ├── WebApiUtility.cs        - API client wrapper
│   │   ├── APSIMBuildsAPIUtility.cs - APSIM builds API integration
│   │   ├── DownloadAccessState.cs  - Download state management
│   │   └── Models/                 (Utility Models)
│   │       ├── UserResponseModel.cs
│   │       ├── OrganisationResponseModel.cs
│   │       ├── Login.cs
│   │       ├── DownloadTokenValidationResponse.cs
│   │       ├── DownloadAuditResponse.cs
│   │       ├── DownloadEventRequest.cs
│   │       ├── DownloadCsvExportResult.cs
│   │       ├── APSIMNextGenDownloadInfo.cs
│   │       └── APSIMClassicDownloadInfo.cs
│   │
│   ├── Classes/                    (Domain Classes)
│   │   ├── AlertType.cs            - Alert type enumerations
│   │   ├── EmailModel.cs           - Email request model
│   │   └── Utilities/              (Additional utilities)
│   │
│   ├── App.razor                   - Root component (error boundary)
│   ├── Routes.razor                - Route definitions
│   └── _Imports.razor              - Global using statements
│
├── Properties/
│   └── launchSettings.json         - Launch configuration
│
├── wwwroot/                        (Static Assets)
│   ├── css/                        - CSS files
│   ├── js/                         - JavaScript files
│   ├── Images/                     - Product images
│   ├── lib/                        - Third-party libraries (Bootstrap, etc.)
│   └── favicon.png
│
├── Program.cs                      - App startup configuration
├── appsettings.json                - Configuration
├── appsettings.Development.json    - Development overrides
├── Dockerfile                      - Container configuration
└── AcceptanceTestsWebApp.csproj      - Project file
```

## Component Overview

### Pages

**Home.razor** (`/`)
- Introduction to APSIM Next Generation
- Product selection cards with icons
- Links to AcceptanceTests and legacy download

**Register.razor** (`/register/{productId?}`)
- General use AcceptanceTests form
- License pathway decision workflow
- Form validation and submission

**SpecialAcceptanceTests.razor** (`/special`)
- Special use license AcceptanceTests
- Organization details collection
- Annual turnover selection

**Download.razor** (`/download`)
- APSIM download interface
- OS-specific download buttons (Windows, macOS, Linux)
- Access control and audit tracking

**Validate.razor** (`/validate`)
- Classic APSIM validation page
- Legacy download access
- Token verification

**Admin.razor** (`/admin`)
- Admin dashboard
- View registered users and organizations
- Download audit trail

### Reusable Components

**ProductBox.razor** - Displays product information card with buttons
**Card.razor** - Generic card container component
**DecisionCard.razor** - Interactive decision selection card
**DotPointCard.razor** - Card with bullet-point content
**CustomModal.razor** - Modal dialog wrapper
**PdfViewer.razor** - PDF document display component

### Layout

**MainLayout.razor** - Primary layout with sidebar navigation
**NavMenu.razor** - Collapsible navigation menu
**Footer.razor** - Application footer
**ReconnectModal.razor** - Connection loss indicator

### Services

**WebApiUtility** - HTTP client for communicating with AcceptanceTestsWebAPI
**APSIMBuildsAPIUtility** - Integration with APSIM builds information
**DownloadAccessState** - Manages download access state and validation

## Configuration

The app connects to the AcceptanceTestsWebAPI for:
- User AcceptanceTests
- Organization data
- Download tracking
- Authentication

Configure the API base URL in `appsettings.json`.

## Development Notes

- In development, detailed errors are shown by default
- Hot reload is supported via `dotnet watch`
- Blazor Server uses interactive server render mode for real-time UI updates
- Status code pages are configured with re-execution middleware

## Contributing

1. Create a feature branch from `master`
2. Make and test your changes locally using `dotnet watch run`
3. Ensure the page compiles without errors
4. Open a pull request with a clear summary of changes

## Support

For issues or questions, contact the APSIM team.
