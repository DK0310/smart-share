# AMD201 — Advanced Microservices Deployment
## Group Work Assignment Brief: Build a Real-World .NET Core Web Service

| Field | Detail |
|---|---|
| **Subject** | AMD201 — Advanced .NET Development |
| **Assignment Type** | Group Work — maximum 3 members per team |
| **Topics Available** | 3 options — your team picks ONE |
| **Technology Stack** | ASP.NET Core, Vue or React, Docker, GitHub Actions |

---

## Overview

In this assignment, your team will design and build a fully functional, real-world web service using ASP.NET Core. The project mirrors what software engineering teams build professionally — a backend API, a frontend interface, a database, and an automated CI/CD pipeline that deploys your app to the cloud.

You have three topics to choose from. All three share the same technical scope and grading criteria — the difference is the domain and features your team will implement. Pick the one your team finds most interesting.

Each topic is intentionally similar in complexity to a URL Shortener service. If you have built something like Bitly or TinyURL before, you already have a good sense of the scope expected here.

---

## Topic Options

### 01 — Pastebin & Code Snippet Sharer
> Think: Pastebin · GitHub Gist · dpaste

#### What is it?

A Pastebin service lets users paste any text — code, notes, logs, configuration files — and instantly get a short, shareable link. The recipient opens the link and sees the formatted content. This is one of the most widely used developer tools on the internet.

Your version will be a simplified but complete clone with user accounts, expiry controls, and syntax highlighting.

#### Core Features *(required for all grades)*

- Users can paste plain text or code and click a button to generate a short unique link (e.g. `/p/aX9kL`)
- The link displays the paste content to anyone who opens it
- Users can set an expiry time for the paste: **1 hour**, **1 day**, **1 week**, or **Never**
- Pastes marked as **Private** are only accessible by the creator (requires login)
- The frontend (Vue or React) must include: a paste editor, a view page, and a dashboard listing the user's own pastes
- RESTful API endpoints:
  - `POST /pastes`
  - `GET /pastes/{code}`
  - `DELETE /pastes/{code}`
- Input validation: reject empty pastes, enforce a maximum size (e.g. 500 KB)
- Store pastes in a relational database (SQL Server or PostgreSQL)

#### Suggested Architecture

- **Backend:** ASP.NET Core Web API — handles paste creation, retrieval, expiry logic, and user authentication (JWT)
- **Frontend:** Vue or React SPA — paste editor with syntax highlighting (e.g. using highlight.js or Prism.js), paste viewer, user dashboard
- **Database:** One table for `Pastes` (code, content, language, expiry, visibility, owner) and one for `Users`
- **Background Job:** A scheduled task (e.g. Hangfire or a hosted service) that deletes expired pastes automatically

#### Merit & Distinction Additions

| Grade | Feature |
|---|---|
| **Merit** | Syntax highlighting: detect or let the user select the programming language (C#, JavaScript, Python, SQL, etc.) and render with color-coded display on the view page |
| **Merit** | View counter: track how many times each paste has been viewed; display the count on the paste page |
| **Distinction** | Implement a diff viewer: given two paste codes, show a side-by-side comparison highlighting lines that changed |
| **Distinction** | Add a public paste feed showing the most recent public pastes, with pagination |

> **Tip:** Start with just `POST` and `GET` working end-to-end (API → database → frontend display). Add expiry and auth afterwards. Do not start with auth — get the core flow working first.

---

### 02 — Poll & Survey Builder
> Think: Strawpoll · Google Forms lite · Slido

#### What is it?

A Poll Builder lets anyone create a multiple-choice question, share a link, and collect votes from respondents in real time. Think of Strawpoll or the poll feature inside Slido. Your version will include live result updates using SignalR, so results update on screen without refreshing the page.

#### Core Features *(required for all grades)*

- A creator fills in a question and up to 6 answer options, then clicks **Create** to get a short poll link (e.g. `/poll/7fGh2`)
- Anyone with the link can open the poll, select one option, and submit their vote
- Each respondent can only vote once (enforce using browser fingerprint or session cookie — no login required for voters)
- The results page shows a **live bar chart** of current votes, updated in real time using **SignalR WebSocket**
- The creator can close the poll at any time to stop accepting new votes
- RESTful API endpoints:
  - `POST /polls`
  - `GET /polls/{code}`
  - `POST /polls/{code}/vote`
  - `GET /polls/{code}/results`
- The frontend (Vue or React) must include: a poll creation form, a voting page, and a live results page with animated bars
- Store polls and votes in a relational database

#### Suggested Architecture

- **Backend:** ASP.NET Core Web API with a **SignalR Hub** for broadcasting live vote updates to all connected clients
- **Frontend:** Vue or React SPA — poll creation form, voting interface, live results chart (e.g. Chart.js or Recharts)
- **Database:** Two tables:
  - `Polls` (code, question, options, status, created_at)
  - `Votes` (poll_id, option_index, voter_token, voted_at)
- **Real-Time:** When a vote is submitted, the server broadcasts the updated vote count to all clients watching that poll's results page via a SignalR Hub

#### Merit & Distinction Additions

| Grade | Feature |
|---|---|
| **Merit** | Allow the creator to set a poll expiry time; after expiry, voting is automatically closed and the results page shows a final summary banner |
| **Merit** | Add multiple question types: yes/no, rating scale (1–5 stars), and open text answer (stored but not voted on) |
| **Distinction** | Add an analytics dashboard for the creator: votes over time (line chart), peak voting minute, top option trend |
| **Distinction** | Implement anonymous Q&A mode: respondents can submit text questions alongside their vote, and the creator can upvote or pin questions live |

> **Tip:** Implement the full API and voting flow first without SignalR — use plain HTTP polling on the frontend. Once votes work, layer in SignalR to replace the polling with WebSocket updates.

---

### 03 — File & Image Sharing Service
> Think: Imgur · WeTransfer · Filebin

#### What is it?

A file sharing service lets users upload a file or image, get a short shareable link, and optionally set an expiry. Anyone with the link can download or view the file. This is the backend that powers services like WeTransfer, Imgur (for direct image links), and Firefox Send.

Your version will support image preview in the browser, enforce file size limits, and automatically clean up expired files.

#### Core Features *(required for all grades)*

- Users can upload a file (any type, max 10 MB) through a drag-and-drop interface or file picker
- After upload, the system returns a short unique link (e.g. `/f/mK3pX`) and copies it to the clipboard
- Anyone with the link can download the file or view it in the browser if it is an image (JPEG, PNG, GIF, WebP)
- The uploader can set a **download limit** (e.g. max 10 downloads) or an **expiry time**, after which the file is deleted
- RESTful API endpoints:
  - `POST /files` (multipart upload)
  - `GET /files/{code}` (download or preview)
  - `DELETE /files/{code}`
- The frontend (Vue or React) must include: an upload page with drag-and-drop, a file preview/download page, and an upload history page
- Store file metadata in a relational database; store the actual file bytes in cloud storage (e.g. Azure Blob Storage, AWS S3, or Cloudinary) or local disk for development
- Input validation: reject files over the size limit, validate MIME types

#### Suggested Architecture

- **Backend:** ASP.NET Core Web API — receives `multipart/form-data` uploads, stores metadata in DB, saves file to storage, returns a short code
- **Frontend:** Vue or React SPA — drag-and-drop upload (use the HTML5 File API), image preview with inline rendering, download button for non-image files
- **Database:** One table: `Files` (code, original_filename, mime_type, size_bytes, storage_path, max_downloads, download_count, expires_at, created_at)
- **Storage:** During development, save files to `wwwroot/uploads`. For Merit/Distinction, integrate a real cloud storage provider
- **Background Job:** A scheduled hosted service that runs daily and deletes expired or over-limit files from both storage and the database

#### Merit & Distinction Additions

| Grade | Feature |
|---|---|
| **Merit** | Integrate real cloud storage (Azure Blob, AWS S3, or Cloudinary) instead of local disk; files must be served via a signed/temporary URL, not a direct public link |
| **Merit** | Show a real-time upload progress bar using the XMLHttpRequest upload progress event or the Fetch API with ReadableStream |
| **Distinction** | Add password-protected files: the uploader sets a password; anyone opening the link must enter the password before downloading |
| **Distinction** | Generate image thumbnails on the server at upload time (e.g. using ImageSharp) and display the thumbnail on the link page instead of loading the full image |

> **Tip:** Start with local disk storage and a simple file input (no drag-and-drop). Get upload → short link → download working end-to-end. Only add cloud storage and drag-and-drop once the core pipeline is solid.

---

## Shared Requirements *(All Topics)*

Regardless of which topic you choose, **ALL** of the following must be included.

### Application

- The backend must be built with **ASP.NET Core Web API**
- The frontend must be a SPA built with **Vue or React**
- Data must be stored in a **relational database** (SQL Server or PostgreSQL)
- The API must follow **REST conventions** with proper HTTP status codes
- The application must include **unit tests** covering at least the core business logic

### DevOps & Deployment

- The application must be **containerized** using a Dockerfile
- A **CI/CD pipeline** must be implemented using **GitHub Actions**
- The pipeline must automatically **build** the Docker image on every push to `main`
- On a successful build, the pipeline must **push** the image to a container registry (e.g. Docker Hub)
- The pipeline must automatically **deploy** the application to a PaaS platform (e.g. Render, Railway, or Azure App Service)

---

## Deliverables

### 1 — Group Presentation *(70%)*

Your team presents to the class. The presentation must cover:

- A short **demo** of the live, deployed application
- **Architecture overview:** how the frontend, backend, database, and storage connect
- A walkthrough of your **CI/CD pipeline** — show a live push triggering a deploy
- **Code walkthrough** of the most technically interesting part of your implementation
- What was **hardest to build** and how you solved it

Slides are required. Every team member must speak.

### 2 — Individual Report *(30%)*

Each person writes their own report (minimum **500 words**) covering:

- What you personally built or contributed
- A technical challenge you faced and how you resolved it
- What you learned from this project
- An honest **peer assessment** of each team member

### 3 — Source Code Repository

Submit a link to your Git repository. It must include:

- All application source code
- Dockerfile(s)
- GitHub Actions workflow file(s) (`.github/workflows/*.yml`)
- A `README.md` with: project description, setup instructions, architecture diagram or description, and a link to the live deployment

---

## Assessment Criteria

The grading criteria below apply to **all three topics** equally.

| Grade | Points | What is expected |
|---|---|---|
| **Pass** | 5 – 6.5 | All core features of your chosen topic work correctly end-to-end. The frontend connects to the backend. Data is saved to a database. The application is containerized and deployed via an automated CI/CD pipeline. Unit tests are present. The presentation includes a live demo and the report is clearly written. |
| **Merit** | 7 – 8.5 | In addition to Pass: you have implemented at least **two** of the Merit additions listed for your topic. Your CI/CD pipeline includes a linting or static analysis step. You use **multi-stage Docker builds** to reduce image size. Integration tests are included. Your presentation explains the design decisions and trade-offs your team made. |
| **Distinction** | 9 – 10 | In addition to Merit: you have implemented at least **one** Distinction feature for your topic. You demonstrate deep architectural understanding in your presentation — not just what you built, but why you made each technical decision. Your codebase is clean, well-structured, and your README is thorough enough for another developer to run and understand your project. |

> **Important:** A non-working or undeployed application **cannot score above 4**. Make sure your live URL is accessible before presentation day. If deployment fails, fix it — do not present a local-only demo as a substitute for deployment.

---

## Final Note

The best projects are not the most complex ones — they are the ones where **every feature works correctly**, the code is clean, and the team can **clearly explain every decision** they made.
