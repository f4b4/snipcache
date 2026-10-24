# SnipCache

This project should help to collect, edit and find know-how snippets of various kinds.


## Examples of know-how snipptets
- markdown texts describing some sort of recipe, eg. setup as ubuntu docker container with postgres db and keycloak authentication
- sql snipptes, eg. how to do a fast row count over all tables in a ms sql server db
- batch files, powershell script snippets or complete powershell scripts
- markdown tables with keyboard shortcuts
- knowledge from ai chats, mostly markdown texts

## Repositories

### SnipCache (reading-client)

- has to be a public repo on github.com
- the main code repo contains a template to create another instance of this repo
- here are all the snippets saved and manged
- tree structure of folders and text files
- text files are mostly markdown, but could also be .ps1, .sh, .bat, etc. files
- any push into this repo starts the 'Deploy SnipCache' action
    - this action updates GitHub Pages and the search engine index

#### API

- API for AI agents and bots: `Save the essence of this chat to SnipCache.app`
- API for the SnipCache repo to save snippets
- API for the search engine?
    - where should be the search index, it seems it must be an the reading-client

### SnipCacheEditor (edit-server)

- has keycloak authentication
- presents the SnipCache tree with folder and files and the monaco editor to edit the files
- folders and new files are creates with a button and dialog
- after a save the changes are pushed to the SnipCache repo


## Technology

Prefered tech:
- react
- pnpm
- asp.net core 10 or 11 for server logic

- SnipCache
    - web app to read the snippets
    - search capability with a search engine, eg. lucene.net
    - clipboard support of whole files and for code snippets embeded in the markdown
    - use the simplest possible web technology
    - to edit a file a redirect to SnipCacheEditor

## Challenges

How to separate the knowledge base, the folders and files, which should be managed in a git repo on github.com and presented in simple, maybe static, web pages and the logic which is neede for the editor and the search engine?