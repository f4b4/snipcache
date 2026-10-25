import { docsLoader } from "@astrojs/starlight/loaders";
import { docsSchema } from "@astrojs/starlight/schema";
import { defineCollection } from "astro:content";
import { z } from "astro/zod";

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: z.object({
        // Written by scripts/prepare-content.ts; absent on a generated home page.
        snipcache: z
          .object({
            sourcePath: z.string(),
            rawUrl: z.string(),
            kind: z.enum(["markdown", "file"]),
            language: z.string(),
          })
          .optional(),
      }),
    }),
  }),
};
