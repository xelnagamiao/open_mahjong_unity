// Metadata reads have their own server-side deadline. A client-side timeout alone
// does not cancel PostgreSQL work, and a global setting would also affect games.
async function withRecordMetadataQuery(pool, read) {
  const client = await pool.connect();
  let releaseError;
  try {
    await client.query('BEGIN READ ONLY');
    await client.query("SET LOCAL statement_timeout = '5s'");
    const result = await read(client);
    await client.query('COMMIT');
    return result;
  } catch (error) {
    try {
      await client.query('ROLLBACK');
    } catch (rollbackError) {
      releaseError = rollbackError;
    }
    throw error;
  } finally {
    client.release(releaseError);
  }
}

module.exports = { withRecordMetadataQuery };
