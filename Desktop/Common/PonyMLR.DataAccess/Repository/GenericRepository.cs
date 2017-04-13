using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Data.Entity;
using System.Linq.Expressions;

namespace PonyMLR.DataAccess
{
    public class GenericRepository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        internal ponydbEntities context;
        internal DbSet<TEntity> dbSet;

        public GenericRepository(ponydbEntities context)
        {
            this.context = context;
            this.dbSet = context.Set<TEntity>();
        }

        public virtual IEnumerable<TEntity> Get(
            Expression<Func<TEntity, bool>> filter = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
            string includeProperties = "")
        {
            try
            {
                IQueryable<TEntity> query = dbSet;

                if (filter != null)
                {
                query = query.Where(filter);
                }

                foreach (var includeProperty in includeProperties.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    query = query.Include(includeProperty);
                }

                if (orderBy != null)
                {
                    return orderBy(query).ToList();
                }
                else
                {
                    return query.ToList();
                }
            }
            catch
            {
                return null;
            }
        }

        public virtual TEntity GetByID(object id)
        {
            try
            {
                return dbSet.Find(id);
            }
            catch
            {
                return null;
            }
        }

        public virtual void Insert(TEntity entity)
        {
            try
            {
                dbSet.Add(entity);
            }
            catch
            {
                return;
            }
        }

        public virtual void Delete(object id)
        {
            try
            {
                TEntity entityToDelete = dbSet.Find(id);
                Delete(entityToDelete);
            }
            catch
            {
                return;
            }
        }

        public virtual void Delete(TEntity entityToDelete)
        {
            try
            {
                if (context.Entry(entityToDelete).State == EntityState.Detached)
                {
                    dbSet.Attach(entityToDelete);
                }
                dbSet.Remove(entityToDelete);
            }
            catch
            {
                return;
            }
        }

        public virtual void Update(TEntity entityToUpdate)
        {
            try
            {
                dbSet.Attach(entityToUpdate);
                context.Entry(entityToUpdate).State = EntityState.Modified;
            }
            catch
            {
                return;
            }
        }
    }
}
